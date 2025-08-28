using System;
using OptiSolver.NET.Services.Base;

namespace OptiSolver.NET.Analysis
{
    /// <summary>
    /// Utilities to obtain shadow prices π (dual variables y) from solver artifacts.
    /// Prefers solver-provided duals; falls back to y^T = c_B^T B^{-1} when available.
    /// Works for both Revised Simplex and Primal Simplex (tableau) results.
    /// </summary>
    public static class ShadowPriceCalculator
    {
        /// <summary>
        /// Try to read duals directly from a solver result, regardless of engine.
        /// Order corresponds to primal constraints.
        /// </summary>
        public static double[] FromResult(SolutionResult r)
        {
            if (r == null)
                return null;

            // 1) Direct property populated by some solvers
            if (r.DualValues != null && r.DualValues.Length > 0)
                return (double[])r.DualValues.Clone();

            // 2) Revised simplex stores y^T in Info["DualY"]
            if (r.Info != null && r.Info.TryGetValue("DualY", out var yObj) && yObj is double[] y)
                return (double[])y.Clone();

            // 3) Both tableau/revised may export CBasis and BInv → y^T = c_B^T B^{-1}
            if (r.Info != null
                && r.Info.TryGetValue("CBasis", out var cBObj) && cBObj is double[] cB
                && r.Info.TryGetValue("BInv", out var bInvObj) && bInvObj is double[,] BInv)
            {
                return MultiplyRowByMatrix(cB, BInv);
            }

            // 4) Some tableau implementations stash duals in Info["DualValues"]
            if (r.Info != null && r.Info.TryGetValue("DualValues", out var dObj) && dObj is double[] dy)
                return (double[])dy.Clone();

            return null;
        }

        /// <summary>
        /// Legacy helper for callers expecting “revised artifacts only”.
        /// </summary>
        public static double[] FromRevisedArtifacts(SolutionResult r)
        {
            if (r == null)
                return null;

            if (r.Info != null && r.Info.TryGetValue("DualY", out var yObj) && yObj is double[] y)
                return (double[])y.Clone();

            if (r.Info != null
                && r.Info.TryGetValue("CBasis", out var cBObj) && cBObj is double[] cB
                && r.Info.TryGetValue("BInv", out var bInvObj) && bInvObj is double[,] BInv)
            {
                return MultiplyRowByMatrix(cB, BInv);
            }

            return null;
        }

        /// <summary>
        /// Legacy helper used when a Primal Simplex (tableau) run produced an optimal basis.
        /// Now simply defers to FromResult(), which knows how to harvest duals from common fields.
        /// </summary>
        public static double[] TryFromTableau(SolutionResult r) => FromResult(r);

        // --------- small numeric helper ---------
        private static double[] MultiplyRowByMatrix(double[] row, double[,] mat)
        {
            int rlen = row?.Length ?? 0;
            if (rlen == 0 || mat == null)
                return null;

            int m = mat.GetLength(0);
            int n = mat.GetLength(1);
            if (rlen != m) // row must match BInv rows
                return null;

            var res = new double[n];
            for (int j = 0; j < n; j++)
            {
                double s = 0.0;
                for (int i = 0; i < m; i++)
                    s += row[i] * mat[i, j];
                res[j] = s;
            }
            return res;
        }
    }
}