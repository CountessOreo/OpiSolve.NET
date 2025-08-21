using System;
using System.Collections.Generic;
using OptiSolver.NET.Core;

namespace OptiSolver.NET.Services.Base
{
    /// <summary>
    /// Abstract base class for all optimization solvers.
    /// Provides shared defaults, logging capabilities, and convenience predicates.
    /// Implements common functionality to reduce code duplication across solver implementations.
    /// </summary>
    public abstract class SolverBase : ISolver
    {
        #region Constants
        /// <summary>
        /// Default numerical tolerance for floating-point comparisons
        /// </summary>
        protected const double EPSILON = 1e-10;

        /// <summary>
        /// Default maximum number of iterations for iterative algorithms
        /// </summary>
        protected const int MAX_ITERATIONS = 10_000;
        #endregion

        #region Abstract Properties
        /// <summary>
        /// Name of the specific algorithm implementation
        /// </summary>
        public abstract string AlgorithmName { get; }

        /// <summary>
        /// Description of the solver's capabilities and characteristics
        /// </summary>
        public abstract string Description { get; }
        #endregion

        #region Abstract Methods
        /// <summary>
        /// Core solving method that must be implemented by derived classes
        /// </summary>
        /// <param name="model">The LP model to solve</param>
        /// <param name="options">Solver options</param>
        /// <returns>Solution result</returns>
        public abstract SolutionResult Solve(LPModel model, Dictionary<string, object> options = null);
        #endregion

        #region Virtual Methods
        /// <summary>
        /// Default implementation for checking if a model can be solved.
        /// Performs basic validation checks that apply to most LP solvers.
        /// </summary>
        /// <param name="model">The model to validate</param>
        /// <returns>True if the model appears solvable</returns>
        public virtual bool CanSolve(LPModel model)
        {
            if (model == null)
                return false;

            // Check for variables
            if (model.Variables == null || model.Variables.Count == 0)
                return false;

            // Check for constraints (null is acceptable for unconstrained problems)
            if (model.Constraints == null)
                return false;

            // Additional validation could be added here by derived classes
            return true;
        }

        /// <summary>
        /// Provides default options that work for most LP solvers.
        /// Derived classes can override to provide algorithm-specific defaults.
        /// </summary>
        /// <returns>Dictionary of default options</returns>
        public virtual Dictionary<string, object> GetDefaultOptions() => new()
        {
            { "MaxIterations", MAX_ITERATIONS },
            { "Tolerance", EPSILON },
            { "Verbose", false },
            { "TimeLimit", 300.0 }, // 5 minutes default
            { "Presolve", true }
        };
        #endregion

        #region Protected Helper Methods
        /// <summary>
        /// Merges user-provided options with default options.
        /// User options take precedence over defaults.
        /// </summary>
        /// <param name="options">User-provided options</param>
        /// <returns>Merged options dictionary</returns>
        protected Dictionary<string, object> MergeOptions(Dictionary<string, object> options)
        {
            var merged = GetDefaultOptions();
            if (options != null)
            {
                foreach (var kv in options)
                    merged[kv.Key] = kv.Value;
            }
            return merged;
        }

        /// <summary>
        /// Logs debug messages when verbose mode is enabled.
        /// Helps with algorithm debugging and performance analysis.
        /// </summary>
        /// <param name="message">Message to log</param>
        /// <param name="options">Options dictionary to check for verbose flag</param>
        protected void LogDebug(string message, Dictionary<string, object> options = null)
        {
            if (options != null &&
                options.TryGetValue("Verbose", out var v) &&
                v is bool verbose && verbose)
            {
                Console.WriteLine($"[{AlgorithmName}] {message}");
            }
        }

        /// <summary>
        /// Logs information messages regardless of verbose setting.
        /// Used for important solver status updates.
        /// </summary>
        /// <param name="message">Message to log</param>
        protected void LogInfo(string message)
        {
            Console.WriteLine($"[{AlgorithmName}] {message}");
        }

        /// <summary>
        /// Validates model before solving and returns appropriate error result if invalid.
        /// </summary>
        /// <param name="model">Model to validate</param>
        /// <returns>Error result if invalid, null if valid</returns>
        protected SolutionResult ValidateModel(LPModel model)
        {
            if (model == null)
                return SolutionResult.CreateError(AlgorithmName, "Model cannot be null");

            if (model.Variables == null || model.Variables.Count == 0)
                return SolutionResult.CreateError(AlgorithmName, "Model must have at least one variable");

            if (model.Constraints == null)
                return SolutionResult.CreateError(AlgorithmName, "Model constraints cannot be null");

            return null; // Model is valid
        }

        /// <summary>
        /// Gets an option value with a default fallback and type checking.
        /// </summary>
        /// <typeparam name="T">Expected type of the option</typeparam>
        /// <param name="options">Options dictionary</param>
        /// <param name="key">Option key</param>
        /// <param name="defaultValue">Default value if key not found or wrong type</param>
        /// <returns>Option value or default</returns>
        protected T GetOption<T>(Dictionary<string, object> options, string key, T defaultValue)
        {
            if (options?.TryGetValue(key, out var value) == true && value is T typedValue)
                return typedValue;
            return defaultValue;
        }
        #endregion

        #region Numerical Helper Methods
        /// <summary>
        /// Checks if a value is effectively zero within tolerance
        /// </summary>
        /// <param name="value">Value to check</param>
        /// <param name="tolerance">Tolerance for comparison (default: EPSILON)</param>
        /// <returns>True if value is within tolerance of zero</returns>
        protected static bool IsZero(double value, double tolerance = EPSILON) => Math.Abs(value) < tolerance;

        /// <summary>
        /// Checks if a value is significantly positive
        /// </summary>
        /// <param name="value">Value to check</param>
        /// <param name="tolerance">Tolerance for comparison (default: EPSILON)</param>
        /// <returns>True if value is greater than tolerance</returns>
        protected static bool IsPositive(double value, double tolerance = EPSILON) => value > tolerance;

        /// <summary>
        /// Checks if a value is significantly negative
        /// </summary>
        /// <param name="value">Value to check</param>
        /// <param name="tolerance">Tolerance for comparison (default: EPSILON)</param>
        /// <returns>True if value is less than negative tolerance</returns>
        protected static bool IsNegative(double value, double tolerance = EPSILON) => value < -tolerance;

        /// <summary>
        /// Compares two double values for equality within tolerance
        /// </summary>
        /// <param name="a">First value</param>
        /// <param name="b">Second value</param>
        /// <param name="tolerance">Tolerance for comparison (default: EPSILON)</param>
        /// <returns>True if values are equal within tolerance</returns>
        protected static bool AreEqual(double a, double b, double tolerance = EPSILON) => Math.Abs(a - b) < tolerance;

        /// <summary>
        /// Safely divides two numbers, handling near-zero denominators
        /// </summary>
        /// <param name="numerator">Numerator</param>
        /// <param name="denominator">Denominator</param>
        /// <param name="tolerance">Tolerance for zero check</param>
        /// <returns>Division result or double.PositiveInfinity/NegativeInfinity for zero denominators</returns>
        protected static double SafeDivide(double numerator, double denominator, double tolerance = EPSILON)
        {
            if (IsZero(denominator, tolerance))
                return numerator >= 0 ? double.PositiveInfinity : double.NegativeInfinity;
            return numerator / denominator;
        }
        #endregion
    }
}