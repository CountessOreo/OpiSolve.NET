using System;
using System.Linq;
using OptiSolver.NET.Services.Base;

namespace OptiSolver.NET.Analysis
{
    /// <summary>
    /// Shadow prices (dual π). Prefer y = c_B^T B^{-1} from Revised Simplex (Info["DualY"]).
    /// Fallback: approximate from final tableau if solver supplied it.
    /// </summary>
    public static class ShadowPriceCalculator
    {
        public static double[] FromRevisedArtifacts(SolutionResult r)
        {
            if (r?.Info != null && r.Info.TryGetValue("DualY", out var yObj) && yObj is double[] y)
                return (double[])y.Clone();
            return null;
        }

        public static double[] TryFromTableau(SolutionResult r)
        {
            // If final tableau is present, we can attempt to recover dual row.
            // Convention in PrimalSimplexTableauSolver: row 0 holds reduced-cost/objective row.
            if (r?.Info == null)
                return null;
            if (!r.Info.TryGetValue("FinalTableau", out var Tobj))
                return null;
            if (Tobj is not double[,] T)
                return null;

            // Heuristic: we can’t reliably parse basis mapping without BasisIndices,
            // so return null unless we also have BasisIndices and the mapping is simple.
            if (!r.Info.TryGetValue("BasisIndices", out var bObj) || bObj is not int[] Bidx)
                return null;

            // Without the original A split, deriving π precisely is brittle. Return null to avoid misleading values.
            return null;
        }
    }
}
