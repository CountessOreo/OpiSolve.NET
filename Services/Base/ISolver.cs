using System.Collections.Generic;
using OptiSolver.NET.Core;

namespace OptiSolver.NET.Services.Base
{
    /// <summary>
    /// Common contract that all solvers must implement.
    /// Defines the standard interface for optimization algorithms.
    /// </summary>
    public interface ISolver
    {
        /// <summary>
        /// Name of the algorithm (e.g., "Primal Simplex", "Dual Simplex", "Interior Point")
        /// </summary>
        string AlgorithmName { get; }

        /// <summary>
        /// Short description of the solver and its capabilities.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Determines if this solver can handle the provided model.
        /// Checks for compatibility with problem structure and constraints.
        /// </summary>
        /// <param name="model">The linear programming model to evaluate</param>
        /// <returns>True if this solver can solve the model, false otherwise</returns>
        bool CanSolve(LPModel model);

        /// <summary>
        /// Solve the linear programming model and return a standardized result.
        /// </summary>
        /// <param name="model">The LP model to solve</param>
        /// <param name="options">Optional solver-specific parameters (MaxIterations, Tolerance, etc.)</param>
        /// <returns>Standardized solution result containing status, objective value, and solution details</returns>
        SolutionResult Solve(LPModel model, Dictionary<string, object> options = null);

        /// <summary>
        /// Returns a dictionary of default solver options with their recommended values.
        /// Common options include MaxIterations, Tolerance, Verbose, etc.
        /// </summary>
        /// <returns>Dictionary containing default option names and values</returns>
        Dictionary<string, object> GetDefaultOptions();
    }
}