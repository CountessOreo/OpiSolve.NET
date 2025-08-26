using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OptiSolver.NET.Core;
using OptiSolver.NET.Services.Base;

namespace OptiSolver.NET.Analysis
{
    /// <summary>
    /// Entry-point for post-optimality analysis.
    /// Works best with RevisedSimplex artifacts (BInv, BasisIndices, DualY, ReducedCosts).
    /// Falls back to final tableau if present (from PrimalSimplexTableauSolver).
    /// </summary>
    public static class SensitivityAnalyser
    {
        public static string BuildReport(LPModel model, SolutionResult result, int preview = 6)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== SENSITIVITY ANALYSIS REPORT ===");

            // 1) Shadow prices (dual variables)
            var (y, basisIdx, BInv, A, c, b, maximize) = ExtractArtifacts(model, result);
            var duals = y ?? ShadowPriceCalculator.TryFromTableau(result);
            if (duals != null)
            {
                sb.AppendLine("\n-- Shadow Prices (π) --");
                sb.AppendLine($"π = [ {string.Join(", ", duals.Select(v => v.ToString("0.000")))} ]");
            }
            else
                sb.AppendLine("\n-- Shadow Prices (π) -- not available");

            // 2) Ranges for non-basic variables (objective coefficients)
            var nonBasicRanges = RangeAnalyser.CostRangesForNonBasic(model, result);
            sb.AppendLine("\n-- Cost Ranges (Non-Basic vars) --");
            if (nonBasicRanges?.Count > 0)
            {
                foreach (var r in nonBasicRanges.Take(preview))
                    sb.AppendLine($"x{r.VarIndex + 1}: c in [{Fmt(r.Min)}, {Fmt(r.Max)}], Δ+={Fmt(r.MaxIncrease)}, Δ-={Fmt(r.MaxDecrease)}  (rc={Fmt(r.ReducedCost)})");
                if (nonBasicRanges.Count > preview)
                    sb.AppendLine($"... (+{nonBasicRanges.Count - preview} more)");
            }
            else
                sb.AppendLine("No non-basic ranges computed.");

            // 3) Ranges for basic variables (objective coefficients)
            var basicRanges = RangeAnalyser.CostRangesForBasic(model, result);
            sb.AppendLine("\n-- Cost Ranges (Basic vars) --");
            if (basicRanges?.Count > 0)
            {
                foreach (var r in basicRanges.Take(preview))
                    sb.AppendLine($"x{r.VarIndex + 1}: c in [{Fmt(r.Min)}, {Fmt(r.Max)}], Δ+={Fmt(r.MaxIncrease)}, Δ-={Fmt(r.MaxDecrease)}");
                if (basicRanges.Count > preview)
                    sb.AppendLine($"... (+{basicRanges.Count - preview} more)");
            }
            else
                sb.AppendLine("No basic ranges computed.");

            // 4) RHS ranges
            var rhsRanges = RangeAnalyser.RhsRanges(model, result);
            sb.AppendLine("\n-- RHS Ranges --");
            if (rhsRanges?.Count > 0)
            {
                for (int i = 0; i < rhsRanges.Count && i < preview; i++)
                    sb.AppendLine($"b{i + 1}: b in [{Fmt(rhsRanges[i].Min)}, {Fmt(rhsRanges[i].Max)}], Δ+={Fmt(rhsRanges[i].MaxIncrease)}, Δ-={Fmt(rhsRanges[i].MaxDecrease)}  (π={Fmt(duals?[i] ?? double.NaN)})");
                if (rhsRanges.Count > preview)
                    sb.AppendLine($"... (+{rhsRanges.Count - preview} more)");
            }
            else
                sb.AppendLine("No RHS ranges computed.");

            sb.AppendLine("\n-- What-if apply (quick) --");
            sb.AppendLine("Use: SensitivityAnalyser.ApplyRhsChange(...) / ApplyCostChange(...) / EvaluateNewActivity(...) / EvaluateNewConstraint(...)");

            // 5) Duality summary
            var dualSummary = DualityAnalyser.CheckAndSummarize(model, result);
            sb.AppendLine("\n-- Duality --");
            sb.AppendLine(dualSummary);

            return sb.ToString();
        }

        // Quick “apply” helpers (keep current basis)
        public static (double[] xNew, double objNew) ApplyRhsChange(LPModel model, SolutionResult result, int rhsIndex, double delta)
        {
            var (_, basisIdx, BInv, A, c, b, maximize) = ExtractArtifacts(model, result, throwIfMissing: true);
            var m = b.Length;
            if (rhsIndex < 0 || rhsIndex >= m)
                throw new ArgumentOutOfRangeException(nameof(rhsIndex));

            var bNew = (double[])b.Clone();
            bNew[rhsIndex] += delta;

            // x_B' = B^{-1} b'
            var xB = Multiply(BInv, bNew);
            // Build full canonical x' with same basis
            var n = A.GetLength(1);
            var xCanon = new double[n];
            for (int i = 0; i < basisIdx.Length; i++)
                xCanon[basisIdx[i]] = Math.Max(0.0, xB[i]);

            // objective with same c
            var obj = Dot(c, xCanon);
            var xOrig = RemapToOriginal(model, xCanon);
            return (xOrig, obj);
        }

        public static (double[] xNew, double objNew) ApplyCostChange(LPModel model, SolutionResult result, int varIndex, double deltaC)
        {
            var (_, basisIdx, BInv, A, c, b, maximize) = ExtractArtifacts(model, result, throwIfMissing: true);
            var cNew = (double[])c.Clone();
            cNew[varIndex] += deltaC;

            // keep basis ⇒ x_B = B^{-1} b unchanged; only objective changes
            var xB = Multiply(BInv, b);
            var n = A.GetLength(1);
            var xCanon = new double[n];
            for (int i = 0; i < basisIdx.Length; i++)
                xCanon[basisIdx[i]] = Math.Max(0.0, xB[i]);

            var obj = Dot(cNew, xCanon);
            var xOrig = RemapToOriginal(model, xCanon);
            return (xOrig, obj);
        }

        public static (double reducedCost, bool profitable) EvaluateNewActivity(LPModel model, SolutionResult result, double[] aNew, double cNew)
        {
            var (y, _, _, _, _, _, maximize) = ExtractArtifacts(model, result, throwIfMissing: true);
            if (y == null)
                throw new InvalidOperationException("Dual vector not available.");
            if (aNew == null)
                throw new ArgumentNullException(nameof(aNew));

            double yTA = 0.0;
            for (int i = 0; i < y.Length; i++)
                yTA += y[i] * aNew[i];
            double rc = cNew - yTA;
            bool profitable = maximize ? rc > 1e-10 : rc < -1e-10;
            return (rc, profitable);
        }

        public static (double slack, bool alreadyFeasible) EvaluateNewConstraint(LPModel model, SolutionResult result, double[] aNew, double bNew)
        {
            // plug current x* (original space) into new constraint
            var x = result.VariableValues ?? Array.Empty<double>();
            double lhs = 0.0;
            // Map x to canonical row-space if needed: we’ll derive A*x in original constraint space
            // The safest check: if aNew length equals x length, multiply directly (user provides in original variable order).
            int n = Math.Min(aNew?.Length ?? 0, x.Length);
            for (int j = 0; j < n; j++)
                lhs += aNew[j] * x[j];

            double slack = bNew - lhs; // for <= type: slack>=0 ⇒ feasible
            return (slack, slack >= -1e-10);
        }

        // ---------- internals ----------
        private static (double[] y, int[] basisIdx, double[,] BInv, double[,] A, double[] c, double[] b, bool maximize)
            ExtractArtifacts(LPModel model, SolutionResult res, bool throwIfMissing = false)
        {
            var canon = new ModelCanonicalTransformer().Canonicalize(model);
            var A = canon.ConstraintMatrix;            // m x n
            var b = canon.RightHandSide;               // m
            var c = (double[])canon.ObjectiveCoefficients.Clone();
            bool maximize = canon.ObjectiveType == ObjectiveType.Maximize;

            double[] y = null;
            int[] basisIdx = null;
            double[,] BInv = null;

            if (res?.Info != null)
            {
                if (res.Info.TryGetValue("DualY", out var yObj) && yObj is double[] yv)
                    y = yv;
                if (res.Info.TryGetValue("BasisIndices", out var bObj) && bObj is int[] bi)
                    basisIdx = bi;
                if (res.Info.TryGetValue("BInv", out var binvObj) && binvObj is double[,] mtx)
                    BInv = mtx;
            }

            if (throwIfMissing)
            {
                if (basisIdx == null || BInv == null)
                    throw new InvalidOperationException("Sensitivity requires BasisIndices and BInv (run Revised Simplex).");
            }
            return (y, basisIdx, BInv, A, c, b, maximize);
        }

        private static double[] Multiply(double[,] M, double[] v)
        {
            var r = M.GetLength(0);
            var c = M.GetLength(1);
            var res = new double[r];
            for (int i = 0; i < r; i++)
                for (int j = 0; j < c; j++)
                    res[i] += M[i, j] * v[j];
            return res;
        }
        private static double Dot(double[] a, double[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            double s = 0.0;
            for (int i = 0; i < n; i++)
                s += a[i] * b[i];
            return s;
        }
        private static double[] RemapToOriginal(LPModel model, double[] xCanon)
        {
            var canon = new ModelCanonicalTransformer().Canonicalize(model);
            return canon.VariableMapping.GetOriginalSolution(xCanon);
        }
        private static string Fmt(double v) => double.IsInfinity(v) ? "±∞" : v.ToString("0.000");
    }
}
