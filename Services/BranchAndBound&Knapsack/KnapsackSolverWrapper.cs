using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OptiSolver.NET.Core;
using OptiSolver.NET.Services.Base;

namespace OptiSolver.NET.Services.BranchAndBound
{
    /// <summary>
    /// Simplified wrapper for the Branch and Bound Knapsack solver
    /// Provides easy-to-use methods for solving knapsack problems
    /// </summary>
    public class KnapsackSolverWrapper
    {
        private readonly BranchBoundKnapsack _solver;

        public KnapsackSolverWrapper()
        {
            _solver = new BranchBoundKnapsack();
        }

        /// <summary>
        /// Solve a knapsack problem with the given items and capacity
        /// </summary>
        /// <param name="values">Item values</param>
        /// <param name="weights">Item weights</param>
        /// <param name="capacity">Knapsack capacity</param>
        /// <param name="verbose">Enable detailed logging</param>
        /// <returns>Solution result with optimal selection</returns>
        public KnapsackSolutionResult SolveKnapsack(double[] values, double[] weights, double capacity, bool verbose = false)
        {
            if (values == null || weights == null)
                throw new ArgumentException("Values and weights cannot be null");

            if (values.Length != weights.Length)
                throw new ArgumentException("Values and weights arrays must have the same length");

            if (capacity < 0)
                throw new ArgumentException("Capacity must be non-negative");

            // Create LP model from knapsack parameters
            var model = CreateKnapsackModel(values, weights, capacity);

            // Set options
            var options = new Dictionary<string, object>
            {
                ["Verbose"] = verbose,
                ["MaxIterations"] = 1000
            };

            // Solve using branch and bound
            var result = _solver.Solve(model, options);

            // Convert to knapsack-specific result
            return new KnapsackSolutionResult
            {
                IsOptimal = result.IsOptimal,
                OptimalValue = result.IsOptimal ? result.ObjectiveValue : 0,
                SelectedItems = result.IsOptimal ?
                    result.VariableValues.Select((v, i) => v > 0.5 ? i : -1).Where(i => i >= 0).ToList() :
                    new List<int>(),
                TotalWeight = result.IsOptimal ?
                    result.VariableValues.Select((v, i) => v > 0.5 ? weights[i] : 0).Sum() :
                    0,
                Iterations = result.Iterations,
                SolveTimeMs = result.SolveTimeMs,
                NodesCreated = result.Info.ContainsKey("NodesCreated") ? (int)result.Info["NodesCreated"] : 0,
                NodesExplored = result.Info.ContainsKey("NodesExplored") ? (int)result.Info["NodesExplored"] : 0,
                NodesFathomed = result.Info.ContainsKey("NodesFathomed") ? (int)result.Info["NodesFathomed"] : 0,
                IterationLog = result.Info.ContainsKey("IterationLog") ? result.Info["IterationLog"].ToString() : "",
                ErrorMessage = !result.IsOptimal ? result.Message : null
            };
        }

        /// <summary>
        /// Create LP model from knapsack parameters
        /// </summary>
        private LPModel CreateKnapsackModel(double[] values, double[] weights, double capacity)
        {
            int n = values.Length;

            // Create model with maximize objective
            var model = new LPModel("Knapsack Problem", ObjectiveType.Maximize);

            // Create variables (all binary)
            for (int i = 0; i < n; i++)
            {
                model.AddVariable($"x{i}", values[i], VariableType.Binary);
            }

            // Create capacity constraint
            model.AddConstraint("Capacity", weights.ToList(), ConstraintRelation.LessThanOrEqual, capacity);

            return model;
        }

        /// <summary>
        /// Display solution in a formatted way
        /// </summary>
        public string FormatSolution(KnapsackSolutionResult result, double[] values, double[] weights)
        {
            var sb = new StringBuilder();

            sb.AppendLine("=== KNAPSACK SOLUTION ===");
            if (result.IsOptimal)
            {
                sb.AppendLine($"Status: OPTIMAL");
                sb.AppendLine($"Optimal Value: {result.OptimalValue:F3}");
                sb.AppendLine($"Total Weight: {result.TotalWeight:F3}");
                sb.AppendLine($"Selected Items: {result.SelectedItems.Count}");
                sb.AppendLine();

                sb.AppendLine("Items Selected:");
                foreach (var itemIndex in result.SelectedItems.OrderBy(x => x))
                {
                    sb.AppendLine($"  Item {itemIndex}: Value={values[itemIndex]:F3}, Weight={weights[itemIndex]:F3}");
                }

                sb.AppendLine();
                sb.AppendLine("Algorithm Statistics:");
                sb.AppendLine($"  Solve Time: {result.SolveTimeMs:F2} ms");
                sb.AppendLine($"  Iterations: {result.Iterations}");
                sb.AppendLine($"  Nodes Created: {result.NodesCreated}");
                sb.AppendLine($"  Nodes Explored: {result.NodesExplored}");
                sb.AppendLine($"  Nodes Fathomed: {result.NodesFathomed}");
            }
            else
            {
                sb.AppendLine($"Status: NOT OPTIMAL");
                sb.AppendLine($"Error: {result.ErrorMessage}");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Simplified result structure for knapsack problems
    /// </summary>
    public class KnapsackSolutionResult
    {
        public bool IsOptimal { get; set; }
        public double OptimalValue { get; set; }
        public List<int> SelectedItems { get; set; } = new List<int>();
        public double TotalWeight { get; set; }
        public int Iterations { get; set; }
        public double SolveTimeMs { get; set; }
        public int NodesCreated { get; set; }
        public int NodesExplored { get; set; }
        public int NodesFathomed { get; set; }
        public string IterationLog { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Example usage and test cases for the knapsack solver
    /// </summary>
    public static class KnapsackExamples
    {
        /// <summary>
        /// Test the solver with the example from the project requirements
        /// max +2 +3 +3 +5 +2 +4
        /// +11 +8 +6 +14 +10 +10 <= 40
        /// bin bin bin bin bin bin
        /// </summary>
        public static void RunProjectExample()
        {
            var solver = new KnapsackSolverWrapper();

            // Project example data
            double[] values = { 2, 3, 3, 5, 2, 4 };
            double[] weights = { 11, 8, 6, 14, 10, 10 };
            double capacity = 40;

            Console.WriteLine("=== PROJECT EXAMPLE ===");
            Console.WriteLine($"Items: {values.Length}");
            Console.WriteLine($"Capacity: {capacity}");
            Console.WriteLine("\nItem Details:");
            for (int i = 0; i < values.Length; i++)
            {
                var ratio = weights[i] > 0 ? values[i] / weights[i] : 0;
                Console.WriteLine($"  Item {i}: Value={values[i]}, Weight={weights[i]}, Ratio={ratio:F3}");
            }

            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine("SOLVING...\n");

            var result = solver.SolveKnapsack(values, weights, capacity, verbose: true);
            var formattedResult = solver.FormatSolution(result, values, weights);

            Console.WriteLine(formattedResult);

            if (result.IsOptimal && !string.IsNullOrEmpty(result.IterationLog))
            {
                Console.WriteLine("\n=== DETAILED ITERATION LOG ===");
                Console.WriteLine(result.IterationLog);
            }
        }

        /// <summary>
        /// Run a smaller example for testing
        /// </summary>
        public static void RunSimpleExample()
        {
            var solver = new KnapsackSolverWrapper();

            // Simple example
            double[] values = { 10, 20, 30 };
            double[] weights = { 1, 1, 1 };
            double capacity = 2;

            Console.WriteLine("=== SIMPLE EXAMPLE ===");
            Console.WriteLine("Values: [10, 20, 30]");
            Console.WriteLine("Weights: [1, 1, 1]");
            Console.WriteLine("Capacity: 2");
            Console.WriteLine("Expected: Select items 1 and 2 (values 20+30=50)");

            var result = solver.SolveKnapsack(values, weights, capacity, verbose: false);
            Console.WriteLine("\n" + solver.FormatSolution(result, values, weights));
        }
    }
}