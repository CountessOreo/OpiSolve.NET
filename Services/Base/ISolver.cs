using OptiSolver.NET.Core;

namespace OptiSolver.NET.Services.Base
{
    /// <summary>
    /// Common contract that all solvers must implement.
    /// </summary>
    public interface ISolver
    {
        // Name of the algorithm (e.g., "Primal Simplex")
        string AlgorithmName { get; }

        // Short description of the solver.
        string Description { get; }

        // True if this solver can handle the provided model
        bool CanSolve(LPModel model);

        /// Solve the model and return a standardized result.
        SolutionResult Solve(LPModel model, Dictionary<string, object> options = null);

        // Returns a dictionary of default solver options
        Dictionary<string, object> GetDefaultOptions();
    }
}
