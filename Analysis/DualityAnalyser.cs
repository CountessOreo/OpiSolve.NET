using System;
using System.Linq;
using System.Text;
using OptiSolver.NET.Core;
using OptiSolver.NET.Services.Base;
using OptiSolver.NET.Services.Simplex;

namespace OptiSolver.NET.Analysis
{
    /// <summary>
    /// Builds/summarizes the dual, solves it via RevisedSimplex, and checks strong/weak duality.
    /// </summary>
    public static class DualityAnalyser
    {
        public static string CheckAndSummarize(LPModel primal, SolutionResult primalRes)
        {
            try
            {
                var dual = BuildDual(primal);
                var solver = new RevisedSimplexSolver();
                var dualRes = solver.Solve(dual);

                var sb = new StringBuilder();
                sb.AppendLine("Dual model solved with Revised Simplex.");
                sb.AppendLine($"Dual status: {dualRes.Status}, z*={dualRes.ObjectiveValue:0.000}");

                if (primalRes.IsOptimal && dualRes.IsOptimal)
                {
                    var pObj = primalRes.ObjectiveValue;
                    var dObj = dualRes.ObjectiveValue;

                    bool strong = Math.Abs(pObj - dObj) < 1e-6 &&
                                  primal.ObjectiveType == ObjectiveType.Maximize;
                    // If primal is minimize, equal optimums still indicate strong duality (sign sense differs).
                    if (primal.ObjectiveType == ObjectiveType.Minimize)
                        strong = Math.Abs(pObj - dObj) < 1e-6;

                    sb.AppendLine($"Strong Duality: {(strong ? "YES" : "NO")} (|z_P - z_D|={Math.Abs(pObj - dObj):0.000000})");
                }
                else
                {
                    sb.AppendLine("Weak Duality holds (always). One/both models not optimal.");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Duality check failed: {ex.Message}";
            }
        }

        // Basic dual builder for: max c^T x, s.t. A x <= b, x >= 0
        // Converts all constraints and bounds accordingly. More exotic cases (>=, =) are handled by canonical transformer.
        public static LPModel BuildDual(LPModel primal)
        {
            var canon = new ModelCanonicalTransformer().Canonicalize(primal); // standardize
            int m = canon.ConstraintCount;
            int n = canon.TotalVariables;
            var A = canon.ConstraintMatrix;
            var b = canon.RightHandSide;
            var c = (double[])canon.ObjectiveCoefficients.Clone();

            // Dual: minimize b^T y, s.t. A^T y >= c (if primal is max with <=)
            var dual = new LPModel("Dual", ObjectiveType.Minimize);
            // y variables (one per row)
            for (int i = 0; i < m; i++)
                dual.AddVariable(b[i], VariableType.Positive); // objective coefficients are b_i

            // constraints: A^T y >= c  → multiply by -1 to use <= form if needed
            for (int j = 0; j < n; j++)
            {
                var col = new double[m];
                for (int i = 0; i < m; i++)
                    col[i] = A[i, j];

                // We’ll store ≥ by negating both sides to fit ConstraintRelation.GreaterOrEqual if supported in LPModel
                // LPModel supports relations directly, so we can add as ≥
                dual.AddConstraint(col.ToList(), ConstraintRelation.GreaterThanOrEqual, c[j]);
            }

            // y >= 0 (already by variable types)
            dual.ValidateModel();
            return dual;
        }
    }
}
