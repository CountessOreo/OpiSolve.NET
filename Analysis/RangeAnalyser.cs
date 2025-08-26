using System;
using System.Collections.Generic;
using System.Linq;
using OptiSolver.NET.Core;
using OptiSolver.NET.Services.Base;

namespace OptiSolver.NET.Analysis
{
    /// <summary>
    /// Cost coefficient ranges for basic/non-basic variables and RHS ranges using current basis.
    /// Formulas assume standard canonical form (x >= 0, Ax = b, maximize by default).
    /// </summary>
    public static class RangeAnalyser
    {
        public static List<SensitivityRange> CostRangesForNonBasic(LPModel model, SolutionResult result)
        {
            // For a non-basic j with x_j = 0 at optimality:
            // Maximize: reduced cost r_j = c_j - y^T A_j <= 0.
            // Allowable INCREASE Δ: keep r_j + Δ <= 0 -> Δ_max = -r_j.
            // Allowable DECREASE often unbounded (until another nonbasic ties; we return +∞).
            var ranges = new List<SensitivityRange>();
            if (result?.Info == null)
                return ranges;

            if (!result.Info.TryGetValue("ReducedCosts", out var rObj) || rObj is not double[] rc)
                rc = result.ReducedCosts;

            var canon = new ModelCanonicalTransformer().Canonicalize(model);
            var (_, Bidx, _, A, c, b, maximize) = Extract(model, result);
            if (Bidx == null || rc == null)
                return ranges;

            var basicSet = new HashSet<int>(Bidx);
            for (int j = 0; j < rc.Length; j++)
            {
                if (basicSet.Contains(j))
                    continue; // skip basics
                var rj = rc[j];
                var sr = new SensitivityRange { VarIndex = j, ReducedCost = rj, IsBasic = false };

                if (maximize)
                {
                    // increase limited by -rj, decrease unbounded
                    double inc = Math.Max(0.0, -rj);
                    sr.Min = double.NegativeInfinity;
                    sr.Max = c[j] + inc;
                    sr.MaxIncrease = inc;
                    sr.MaxDecrease = double.PositiveInfinity;
                }
                else
                {
                    // minimize: r_j >= 0 at optimum; Δ_min = -rj (allowed decrease), increase often unbounded
                    double dec = Math.Max(0.0, rj);
                    sr.Min = c[j] - dec;
                    sr.Max = double.PositiveInfinity;
                    sr.MaxIncrease = double.PositiveInfinity;
                    sr.MaxDecrease = dec;
                }
                ranges.Add(sr);
            }
            return ranges;
        }

        public static List<SensitivityRange> CostRangesForBasic(LPModel model, SolutionResult result)
        {
            // For a basic var k: change c_k by δ.
            // y = c_B^T B^{-1}, r_N = c_N - y A_N must retain sign (≤0 for max, ≥0 for min).
            // Changing c_k shifts y by δ * e_k^T B^{-1}, so r_N shifts by -δ * (e_k^T B^{-1} A_N) = -δ * w
            // We bound δ so that all r_N keep sign. Implement with w = row k of B^{-1} times A_N.
            var output = new List<SensitivityRange>();
            var (y, Bidx, BInv, A, c, b, maximize) = Extract(model, result);
            if (Bidx == null || BInv == null)
                return output;

            int m = Bidx.Length;
            int n = A.GetLength(1);
            var basicSet = new HashSet<int>(Bidx);

            // Build A_N and set of N
            var nonbasic = Enumerable.Range(0, n).Where(j => !basicSet.Contains(j)).ToArray();
            if (!result.Info.TryGetValue("ReducedCosts", out var rObj) || rObj is not double[] rc)
                rc = result.ReducedCosts;

            for (int kPos = 0; kPos < m; kPos++)
            {
                int jBasic = Bidx[kPos];
                var sr = new SensitivityRange { VarIndex = jBasic, IsBasic = true };

                // w = e_k^T B^{-1} A_N  (length = |N|)
                var w = new double[nonbasic.Length];
                for (int idx = 0; idx < nonbasic.Length; idx++)
                {
                    int j = nonbasic[idx];
                    double sum = 0.0;
                    for (int i = 0; i < m; i++)
                        sum += BInv[kPos, i] * A[i, j];
                    w[idx] = sum;
                }

                // Bounds on δ so that r_N' keeps sign constraint
                // Maximize: want r_N' = r_N - δ * w <= 0  ⇒
                //   if w>0 ⇒ δ >= r_N / w  (upper/lower depends on sign)
                //   if w<0 ⇒ δ <= r_N / w
                double low = double.NegativeInfinity;
                double high = double.PositiveInfinity;

                for (int idx = 0; idx < nonbasic.Length; idx++)
                {
                    int j = nonbasic[idx];
                    double rj = rc?[j] ?? (0.0); // if missing, assume 0 to be safe
                    double wj = w[idx];

                    if (Math.Abs(wj) < 1e-12)
                        continue;

                    double bound = rj / wj;
                    if (maximize)
                    {
                        if (wj > 0)
                            low = Math.Max(low, bound);
                        else
                            high = Math.Min(high, bound);
                    }
                    else
                    {
                        // minimize: r_N' = r_N - δ w >= 0
                        if (wj > 0)
                            high = Math.Min(high, bound);
                        else
                            low = Math.Max(low, bound);
                    }
                }

                sr.Min = double.IsNaN(low) ? double.NegativeInfinity : low + c[jBasic];
                sr.Max = double.IsNaN(high) ? double.PositiveInfinity : high + c[jBasic];
                sr.MaxDecrease = double.IsInfinity(low) ? double.PositiveInfinity : (c[jBasic] - (low + c[jBasic]));
                sr.MaxIncrease = double.IsInfinity(high) ? double.PositiveInfinity : ((high + c[jBasic]) - c[jBasic]);
                output.Add(sr);
            }
            return output;
        }

        public static List<SensitivityRange> RhsRanges(LPModel model, SolutionResult result)
        {
            // Feasibility ranges: x_B = B^{-1} b stays >= 0.
            // For changing a single b_i by Δ, let d = B^{-1} e_i (column i of B^{-1}).
            // For each basic component t: x_Bt' = x_Bt + Δ * d_t >= 0 ⇒
            //   if d_t > 0 ⇒ Δ >= -x_Bt / d_t
            //   if d_t < 0 ⇒ Δ <= -x_Bt / d_t
            var list = new List<SensitivityRange>();
            var (_, Bidx, BInv, A, c, b, _) = Extract(model, result);
            if (BInv == null || Bidx == null)
                return list;

            int m = b.Length;
            // current x_B
            var xB = Multiply(BInv, b);

            for (int i = 0; i < m; i++)
            {
                var sr = new SensitivityRange { RhsIndex = i, IsRhs = true };
                double low = double.NegativeInfinity, high = double.PositiveInfinity;

                for (int t = 0; t < m; t++)
                {
                    double d_t = BInv[t, i];
                    if (Math.Abs(d_t) < 1e-12)
                        continue;

                    double bound = -xB[t] / d_t;
                    if (d_t > 0)
                        low = Math.Max(low, bound);
                    else
                        high = Math.Min(high, bound);
                }

                sr.Min = double.IsInfinity(low) ? double.NegativeInfinity : (b[i] + low);
                sr.Max = double.IsInfinity(high) ? double.PositiveInfinity : (b[i] + high);
                sr.MaxDecrease = double.IsInfinity(low) ? double.PositiveInfinity : -low;
                sr.MaxIncrease = double.IsInfinity(high) ? double.PositiveInfinity : high;
                list.Add(sr);
            }
            return list;
        }

        // ---------- internals ----------
        private static (double[] y, int[] Bidx, double[,] BInv, double[,] A, double[] c, double[] b, bool maximize)
            Extract(LPModel model, SolutionResult result)
        {
            var canon = new ModelCanonicalTransformer().Canonicalize(model);
            var A = canon.ConstraintMatrix;
            var b = canon.RightHandSide;
            var c = (double[])canon.ObjectiveCoefficients.Clone();
            bool maximize = canon.ObjectiveType == ObjectiveType.Maximize;

            double[] y = null;
            int[] Bidx = null;
            double[,] BInv = null;
            if (result?.Info != null)
            {
                if (result.Info.TryGetValue("DualY", out var yObj) && yObj is double[] yv)
                    y = yv;
                if (result.Info.TryGetValue("BasisIndices", out var bObj) && bObj is int[] bi)
                    Bidx = bi;
                if (result.Info.TryGetValue("BInv", out var binvObj) && binvObj is double[,] mtx)
                    BInv = mtx;
            }
            return (y, Bidx, BInv, A, c, b, maximize);
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
    }

    public sealed class SensitivityRange
    {
        public int VarIndex { get; set; } = -1;     // for cost ranges
        public int RhsIndex { get; set; } = -1;     // for RHS ranges
        public bool IsBasic { get; set; }
        public bool IsRhs { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double MaxIncrease { get; set; }
        public double MaxDecrease { get; set; }
        public double ReducedCost { get; set; }
    }
}
