using OptiSolver.NET.Core;
using OptiSolver.NET.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OptiSolver.NET.Services.Base
{
    /// <summary>
    /// Standardized result returned by solvers.
    /// </summary>
    public class SolutionResult
    {
        // Overall status of the solve
        public SolutionStatus Status { get; set; } = SolutionStatus.NotSolved;

        // Raw solver objective value (typically min-form internally)
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
        /// Sense of the original model (used by display and reporting).
        /// Menu/solvers should stamp this; we also honor Info["ObjectiveSense"] as a fallback.
        /// </summary>
        public ObjectiveType ObjectiveSense { get; set; } = ObjectiveType.Minimize;

        // ---------- Convenience flags ----------
        public bool IsOptimal => Status == SolutionStatus.Optimal;
        public bool IsInfeasible => Status == SolutionStatus.Infeasible;
        public bool IsUnbounded => Status == SolutionStatus.Unbounded;
        public bool IsFeasible => Status == SolutionStatus.Optimal; // kept as in your original
        public bool HasAlternative => Status == SolutionStatus.AlternativeOptimal;

        // ---------- Display helpers ----------
        private ObjectiveType ResolveSense()
        {
            var sense = ObjectiveSense;

            // Allow override from Info["ObjectiveSense"] (enum or string)
            if (Info != null && Info.TryGetValue("ObjectiveSense", out var sObj))
            {
                if (sObj is ObjectiveType sEnum)
                    sense = sEnum;
                else if (sObj is string sStr && Enum.TryParse<ObjectiveType>(sStr, true, out var sParsed))
                    sense = sParsed;
            }

            return sense;
        }

        /// <summary>
        /// User-facing objective value (flip raw min-form to user sense).
        /// </summary>
        public double GetDisplayObjective()
        {
            // Solvers now return user-sense directly (no flips).
            return ObjectiveValue;
        }

        public string GetDisplayObjectiveRounded() =>
            double.IsNaN(ObjectiveValue) ? "NaN" : DisplayHelper.Round3(ObjectiveValue);

        public override string ToString()
        {
            var sb = $"Algorithm: {AlgorithmUsed}\nStatus: {Status}\n";
            if (!string.IsNullOrWhiteSpace(Message))
                sb += $"Message: {Message}\n";

            if (IsOptimal || HasAlternative)
            {
                sb += $"Objective: {ObjectiveValue:F6}\n";
                if (VariableValues != null)
                    sb += $"x*: [{string.Join(", ", VariableValues.Select(v => v.ToString("F6")))}]\n";
                if (HasAlternateOptima || HasAlternative)
                    sb += "Note: Alternate optimal solutions exist.\n";
            }

            sb += $"Iterations: {Iterations}\n";
            sb += $"Solve Time: {SolveTimeMs:F2} ms";
            return sb;
        }

        // ---------- Factory helpers ----------
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
