using OptiSolver.NET.Core;
using System;
using System.Linq;
using System.Collections.Generic;

namespace OptiSolver.NET.Services.Base
{
    /// <summary>
    /// Standardized result returned by solvers.
    /// </summary>
    public class SolutionResult
    {
        // Overall status of the solve
        public SolutionStatus Status { get; set; } = SolutionStatus.NotSolved;

        // Optimal objective value (normalized for display if caller chose to do so)
        public double ObjectiveValue { get; set; }

        // Variable values solution
        public double[] VariableValues { get; set; }

        // Number of iterations performed by the algorithm
        public int Iterations { get; set; }

        // Total solve time in milliseconds
        public double SolveTimeMs { get; set; }

        // Name of the algorithm used (e.g., "Primal Simplex")
        public string AlgorithmUsed { get; set; }

        // Status/details message
        public string Message { get; set; }

        // Extra, solver-specific data (e.g., basis info, bounds, logs)
        public Dictionary<string, object> Info { get; set; } = new();

        // Dual variable values (if applicable)
        public double[] DualValues { get; set; }

        // Shadow prices per constraint (if applicable)
        public double[] ShadowPrices { get; set; }

        // Reduced costs per variable (if applicable)
        public double[] ReducedCosts { get; set; }

        public bool HasAlternateOptima { get; set; }

        /// <summary>
        /// Sense of the original model (used by some UIs; optional).
        /// Note: In Menu.cs we normalize ObjectiveValue already; still useful for reporting.
        /// </summary>
        public ObjectiveType ObjectiveSense { get; set; } = ObjectiveType.Minimize;

        // Convenience checks
        public bool IsOptimal => Status == SolutionStatus.Optimal;
        public bool IsInfeasible => Status == SolutionStatus.Infeasible;
        public bool IsUnbounded => Status == SolutionStatus.Unbounded;
        public bool IsFeasible => Status == SolutionStatus.Optimal;
        public bool HasAlternative => Status == SolutionStatus.AlternativeOptimal;

        public override string ToString()
        {
            var sb = $"Algorithm: {AlgorithmUsed}\nStatus: {Status}\n";
            if (!string.IsNullOrWhiteSpace(Message))
                sb += $"Message: {Message}\n";

            if (IsOptimal)
            {
                sb += $"Objective: {ObjectiveValue:F6}\n";
                if (VariableValues != null)
                    sb += $"x*: [{string.Join(", ", VariableValues.Select(v => v.ToString("F6")))}]\n";
                if (HasAlternateOptima)
                    sb += "Note: Alternate optimal solutions exist.\n";
            }

            sb += $"Iterations: {Iterations}\n";
            sb += $"Solve Time: {SolveTimeMs:F2} ms";
            return sb;
        }

        /// <summary>
        /// Returns the objective as modeled (if caller kept raw solver sign and wants a display-only flip).
        /// If you've already normalized ObjectiveValue (as in Menu.cs), you don't need this.
        /// </summary>
        public double GetDisplayObjective()
            => ObjectiveSense == ObjectiveType.Maximize ? -ObjectiveValue : ObjectiveValue;

        public static SolutionResult CreateOptimal(
            double objectiveValue,
            double[] variableValues,
            int iterations,
            string algorithm,
            double solveTimeMs = 0,
            string message = "Optimal solution found",
            bool hasAlternateOptima = false)
        {
            return new SolutionResult
            {
                Status = hasAlternateOptima ? SolutionStatus.AlternativeOptimal : SolutionStatus.Optimal,
                ObjectiveValue = objectiveValue,
                VariableValues = variableValues,
                Iterations = iterations,
                SolveTimeMs = solveTimeMs,
                AlgorithmUsed = algorithm,
                Message = message,
                HasAlternateOptima = hasAlternateOptima,
            };
        }

        public static SolutionResult CreateInfeasible(
            string algorithm,
            string message = "Model is infeasible")
        {
            return new SolutionResult
            {
                Status = SolutionStatus.Infeasible,
                AlgorithmUsed = algorithm,
                Message = message
            };
        }

        public static SolutionResult CreateUnbounded(
            string algorithm,
            string message = "Objective is unbounded")
        {
            return new SolutionResult
            {
                Status = SolutionStatus.Unbounded,
                AlgorithmUsed = algorithm,
                Message = message
            };
        }

        public static SolutionResult CreateInfeasible(string message = "Model is infeasible")
            => CreateInfeasible("N/A", message);

        public static SolutionResult CreateUnbounded(string message = "Objective is unbounded")
            => CreateUnbounded("N/A", message);

        public static SolutionResult CreateError(
            string algorithm,
            string errorMessage)
        {
            return new SolutionResult
            {
                Status = SolutionStatus.Error,
                AlgorithmUsed = algorithm,
                Message = errorMessage
            };
        }

        public static SolutionResult CreateMaxIterationsReached(
            string algorithm,
            int iterations,
            double objectiveValue = double.NaN,
            double[] variableValues = null,
            string message = "Maximum iterations reached")
        {
            return new SolutionResult
            {
                Status = SolutionStatus.MaxIterations,
                AlgorithmUsed = algorithm,
                Iterations = iterations,
                ObjectiveValue = objectiveValue,
                VariableValues = variableValues,
                Message = message
            };
        }
    }
}
