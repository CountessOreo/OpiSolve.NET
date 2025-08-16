using OptiSolver.NET.Core;
using OptiSolver.NET.Services.Base;
using OptiSolver.NET.Services.BranchAndBound_Knapsack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OptiSolver.NET.Services.BranchAndBound
{
    /// <summary>
    /// Branch and Bound algorithm specifically designed for 0-1 Knapsack problems.
    /// Uses intelligent bounding with fractional knapsack upper bounds and systematic branching.
    /// </summary>
    public class BranchBoundKnapsack : SolverBase
    {
        public override string AlgorithmName => "Branch and Bound Knapsack";
        public override string Description => "Branch and Bound algorithm for binary knapsack problems with backtracking and node fathoming";

        // Algorithm statistics
        private int _nodeCount;
        private int _fathomed;
        private int _explored;
        private List<KnapsackNode> _allNodes;
        private StringBuilder _iterationLog;

        public override bool CanSolve(LPModel model)
        {
            if (!base.CanSolve(model))
                return false;

            // Check if it's a knapsack problem (single constraint, binary variables)
            if (model.Constraints.Count != 1)
                return false;

            // All variables should be binary
            return model.Variables.All(v => v.Type == VariableType.Binary);
        }

        public override SolutionResult Solve(LPModel model, Dictionary<string, object> options = null)
        {
            var startTime = DateTime.Now;
            var mergedOptions = MergeOptions(options);

            // Validate model
            var validationError = ValidateModel(model);
            if (validationError != null)
                return validationError;

            // Initialize algorithm state
            _nodeCount = 0;
            _fathomed = 0;
            _explored = 0;
            _allNodes = new List<KnapsackNode>();
            _iterationLog = new StringBuilder();

            LogDebug("Starting Branch and Bound Knapsack Algorithm", mergedOptions);
            _iterationLog.AppendLine("=== BRANCH AND BOUND KNAPSACK ALGORITHM ===\n");

            try
            {
                // Extract knapsack parameters
                var items = ExtractKnapsackItems(model);
                var capacity = model.Constraints[0].RightHandSide;

                LogDebug($"Knapsack capacity: {capacity}", mergedOptions);
                LogDebug($"Number of items: {items.Count}", mergedOptions);

                _iterationLog.AppendLine($"Knapsack Capacity: {capacity:F3}");
                _iterationLog.AppendLine($"Number of Items: {items.Count}");
                _iterationLog.AppendLine("\nItems (Value, Weight, Ratio):");

                for (int i = 0; i < items.Count; i++)
                {
                    var ratio = items[i].Weight > 0 ? items[i].Value / items[i].Weight : double.MaxValue;
                    _iterationLog.AppendLine($"  Item {i}: Value={items[i].Value:F3}, Weight={items[i].Weight:F3}, Ratio={ratio:F3}");
                }
                _iterationLog.AppendLine();

                // Sort items by value-to-weight ratio (descending) for better bounds
                var sortedIndices = Enumerable.Range(0, items.Count)
                    .OrderByDescending(i => items[i].Weight > 0 ? items[i].Value / items[i].Weight : double.MaxValue)
                    .ToList();

                // Create root node
                var rootNode = new KnapsackNode
                {
                    Id = ++_nodeCount,
                    Level = -1,
                    Value = 0,
                    Weight = 0,
                    UpperBound = CalculateFractionalBound(items, capacity, new bool[items.Count], -1),
                    Solution = new bool[items.Count],
                    IsComplete = false
                };

                _allNodes.Add(rootNode);
                var queue = new PriorityQueue<KnapsackNode, double>();
                queue.Enqueue(rootNode, -rootNode.UpperBound); // Negative for max-heap behavior

                double bestValue = 0;
                bool[] bestSolution = new bool[items.Count];
                var maxIterations = GetOption(mergedOptions, "MaxIterations", MAX_ITERATIONS);

                _iterationLog.AppendLine($"Root Node Upper Bound: {rootNode.UpperBound:F3}\n");
                _iterationLog.AppendLine("=== BRANCH AND BOUND ITERATIONS ===\n");

                int iteration = 0;
                while (queue.Count > 0 && iteration < maxIterations)
                {
                    iteration++;
                    var currentNode = queue.Dequeue();
                    _explored++;

                    LogDebug($"Iteration {iteration}: Exploring Node {currentNode.Id} at level {currentNode.Level}", mergedOptions);

                    _iterationLog.AppendLine($"--- Iteration {iteration} ---");
                    _iterationLog.AppendLine($"Exploring Node {currentNode.Id}:");
                    _iterationLog.AppendLine($"  Level: {currentNode.Level}");
                    _iterationLog.AppendLine($"  Current Value: {currentNode.Value:F3}");
                    _iterationLog.AppendLine($"  Current Weight: {currentNode.Weight:F3}");
                    _iterationLog.AppendLine($"  Upper Bound: {currentNode.UpperBound:F3}");
                    _iterationLog.AppendLine($"  Best Value So Far: {bestValue:F3}");

                    // Fathom by bound
                    if (currentNode.UpperBound <= bestValue + EPSILON)
                    {
                        LogDebug($"Node {currentNode.Id} fathomed by bound", mergedOptions);
                        _iterationLog.AppendLine($"  -> FATHOMED BY BOUND (UB: {currentNode.UpperBound:F3} <= Best: {bestValue:F3})");
                        _fathomed++;
                        continue;
                    }

                    // Check if we've assigned all variables
                    if (currentNode.Level == items.Count - 1)
                    {
                        if (currentNode.Value > bestValue)
                        {
                            bestValue = currentNode.Value;
                            Array.Copy(currentNode.Solution, bestSolution, items.Count);
                            LogDebug($"New best solution found: {bestValue}", mergedOptions);
                            _iterationLog.AppendLine($"  -> NEW BEST SOLUTION FOUND: {bestValue:F3}");
                        }
                        else
                        {
                            _iterationLog.AppendLine($"  -> COMPLETE SOLUTION, NOT BETTER THAN CURRENT BEST");
                        }
                        continue;
                    }

                    // Branch: try including the next item (level + 1)
                    int nextItem = currentNode.Level + 1;

                    // Branch 1: Include the item
                    if (currentNode.Weight + items[nextItem].Weight <= capacity + EPSILON)
                    {
                        var includeNode = new KnapsackNode
                        {
                            Id = ++_nodeCount,
                            Level = nextItem,
                            Value = currentNode.Value + items[nextItem].Value,
                            Weight = currentNode.Weight + items[nextItem].Weight,
                            Solution = (bool[])currentNode.Solution.Clone(),
                            IsComplete = nextItem == items.Count - 1
                        };

                        includeNode.Solution[nextItem] = true;
                        includeNode.UpperBound = CalculateFractionalBound(items, capacity, includeNode.Solution, nextItem);

                        _allNodes.Add(includeNode);

                        _iterationLog.AppendLine($"  Branch 1 - INCLUDE Item {nextItem}:");
                        _iterationLog.AppendLine($"    New Node {includeNode.Id}: Value={includeNode.Value:F3}, Weight={includeNode.Weight:F3}, UB={includeNode.UpperBound:F3}");

                        if (includeNode.UpperBound > bestValue + EPSILON)
                        {
                            queue.Enqueue(includeNode, -includeNode.UpperBound);
                            _iterationLog.AppendLine($"    -> Added to queue");
                        }
                        else
                        {
                            _fathomed++;
                            _iterationLog.AppendLine($"    -> FATHOMED BY BOUND");
                        }
                    }
                    else
                    {
                        _iterationLog.AppendLine($"  Branch 1 - INCLUDE Item {nextItem}: INFEASIBLE (exceeds capacity)");
                    }

                    // Branch 2: Exclude the item
                    var excludeNode = new KnapsackNode
                    {
                        Id = ++_nodeCount,
                        Level = nextItem,
                        Value = currentNode.Value,
                        Weight = currentNode.Weight,
                        Solution = (bool[])currentNode.Solution.Clone(),
                        IsComplete = nextItem == items.Count - 1
                    };

                    excludeNode.Solution[nextItem] = false;
                    excludeNode.UpperBound = CalculateFractionalBound(items, capacity, excludeNode.Solution, nextItem);

                    _allNodes.Add(excludeNode);

                    _iterationLog.AppendLine($"  Branch 2 - EXCLUDE Item {nextItem}:");
                    _iterationLog.AppendLine($"    New Node {excludeNode.Id}: Value={excludeNode.Value:F3}, Weight={excludeNode.Weight:F3}, UB={excludeNode.UpperBound:F3}");

                    if (excludeNode.UpperBound > bestValue + EPSILON)
                    {
                        queue.Enqueue(excludeNode, -excludeNode.UpperBound);
                        _iterationLog.AppendLine($"    -> Added to queue");
                    }
                    else
                    {
                        _fathomed++;
                        _iterationLog.AppendLine($"    -> FATHOMED BY BOUND");
                    }

                    _iterationLog.AppendLine($"  Queue size: {queue.Count}");
                    _iterationLog.AppendLine();
                }

                var solveTime = (DateTime.Now - startTime).TotalMilliseconds;

                _iterationLog.AppendLine("=== FINAL RESULTS ===");
                _iterationLog.AppendLine($"Best Solution Value: {bestValue:F3}");
                _iterationLog.AppendLine($"Total Nodes Created: {_nodeCount}");
                _iterationLog.AppendLine($"Nodes Explored: {_explored}");
                _iterationLog.AppendLine($"Nodes Fathomed: {_fathomed}");
                _iterationLog.AppendLine($"Final Queue Size: {queue.Count}");
                _iterationLog.AppendLine();

                _iterationLog.AppendLine("Optimal Solution:");
                double totalWeight = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    if (bestSolution[i])
                    {
                        totalWeight += items[i].Weight;
                        _iterationLog.AppendLine($"  Include Item {i}: Value={items[i].Value:F3}, Weight={items[i].Weight:F3}");
                    }
                }
                _iterationLog.AppendLine($"Total Weight Used: {totalWeight:F3} / {capacity:F3}");

                // Convert boolean solution back to double array
                var solutionValues = bestSolution.Select(b => b ? 1.0 : 0.0).ToArray();

                var result = SolutionResult.CreateOptimal(
                    bestValue,
                    solutionValues,
                    iteration,
                    AlgorithmName,
                    solveTime,
                    $"Optimal solution found. Nodes: {_nodeCount}, Explored: {_explored}, Fathomed: {_fathomed}"
                );

                // Add algorithm-specific information
                result.Info["NodesCreated"] = _nodeCount;
                result.Info["NodesExplored"] = _explored;
                result.Info["NodesFathomed"] = _fathomed;
                result.Info["IterationLog"] = _iterationLog.ToString();
                result.Info["BranchingTree"] = _allNodes;

                return result;
            }
            catch (Exception ex)
            {
                return SolutionResult.CreateError(AlgorithmName, $"Error during solving: {ex.Message}");
            }
        }

        /// <summary>
        /// Extract knapsack items from the LP model
        /// </summary>
        private List<KnapsackItem> ExtractKnapsackItems(LPModel model)
        {
            var items = new List<KnapsackItem>();
            var constraint = model.Constraints[0];

            for (int i = 0; i < model.Variables.Count; i++)
            {
                var value = model.Variables[i].Coefficient;
                var weight = constraint.Coefficients[i];

                // For minimization, negate the objective
                if (model.ObjectiveType == ObjectiveType.Minimize)
                    value = -value;

                items.Add(new KnapsackItem
                {
                    Index = i,
                    Value = value,
                    Weight = weight
                });
            }

            return items;
        }

        /// <summary>
        /// Calculate fractional knapsack upper bound using greedy approach
        /// </summary>
        private double CalculateFractionalBound(List<KnapsackItem> items, double capacity, bool[] currentSolution, int level)
        {
            double bound = 0;
            double remainingCapacity = capacity;

            // Add value from already selected items
            for (int i = 0; i <= level; i++)
            {
                if (currentSolution[i])
                {
                    bound += items[i].Value;
                    remainingCapacity -= items[i].Weight;
                }
            }

            // Sort remaining items by value-to-weight ratio
            var remainingItems = new List<(int index, double ratio, double value, double weight)>();
            for (int i = level + 1; i < items.Count; i++)
            {
                var ratio = items[i].Weight > 0 ? items[i].Value / items[i].Weight : double.MaxValue;
                remainingItems.Add((i, ratio, items[i].Value, items[i].Weight));
            }

            remainingItems.Sort((a, b) => b.ratio.CompareTo(a.ratio)); // Descending order

            // Greedily add items (fractionally if needed)
            foreach (var item in remainingItems)
            {
                if (remainingCapacity <= 0)
                    break;

                if (item.weight <= remainingCapacity)
                {
                    // Take the whole item
                    bound += item.value;
                    remainingCapacity -= item.weight;
                }
                else
                {
                    // Take fractional part
                    bound += (remainingCapacity / item.weight) * item.value;
                    break;
                }
            }

            return bound;
        }
    }
}