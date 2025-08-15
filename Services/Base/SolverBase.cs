using System;
using System.Collections.Generic;
using OptiSolver.NET.Core;

namespace OptiSolver.NET.Services.Base
{
    /// <summary>
    /// Abstract base class for all optimization solvers
    /// Provides shared defaults, logging, and convenience predicates.
    /// </summary>
    public abstract class SolverBase : ISolver
    {
        // 
        protected const double EPSILON = 1e-10;
        protected const int MAX_ITERATIONS = 10_000;

        public abstract string AlgorithmName { get; }
        public abstract string Description { get; }

        public abstract SolutionResult Solve(LPModel model, Dictionary<string, object> options = null);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public virtual bool CanSolve(LPModel model)
        {
            if (model == null)
                return false;
            if (model.Variables == null || model.Variables.Count == 0)
                return false;
            if (model.Constraints == null)
                return false;
            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public virtual Dictionary<string, object> GetDefaultOptions() => new()
        {
            { "MaxIterations", MAX_ITERATIONS },
            { "Tolerance", EPSILON },
            { "Verbose", false }
        };

        /// <summary>
        /// 
        /// </summary>
        /// <param name="options"></param>
        /// <returns></returns>
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
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="options"></param>
        protected void LogDebug(string message, Dictionary<string, object> options = null)
        {
            if (options != null &&
                options.TryGetValue("Verbose", out var v) &&
                v is bool verbose && verbose)
            {
                Console.WriteLine($"[{AlgorithmName}] {message}");
            }
        }

        // 
        protected static bool IsZero(double value, double tol = EPSILON) => Math.Abs(value) < tol;
        protected static bool IsPositive(double value, double tol = EPSILON) => value > tol;
        protected static bool IsNegative(double value, double tol = EPSILON) => value < -tol;
    }
}
