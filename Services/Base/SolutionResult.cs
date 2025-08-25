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

        // Optimal objective values
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
        /// 
        /// </summary>
        /// <param name="objectiveValue"></param>
        /// <param name="variableValues"></param>
        /// <param name="iterations"></param>
        /// <param name="algorithm"></param>
        /// <param name="solveTimeMs"></param>
        /// <param name="message"></param>
        /// <returns></returns>
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
                HasAlternateOptima = hasAlternateOptima
            };
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="algorithm"></param>
        /// <param name="message"></param>
        /// <returns></returns>
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="algorithm"></param>
        /// <param name="message"></param>
        /// <returns></returns>
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="algorithm"></param>
        /// <param name="errorMessage"></param>
        /// <returns></returns>
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="algorithm"></param>
        /// <param name="iterations"></param>
        /// <param name="objectiveValue"></param>
        /// <param name="variableValues"></param>
        /// <param name="message"></param>
        /// <returns></returns>
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