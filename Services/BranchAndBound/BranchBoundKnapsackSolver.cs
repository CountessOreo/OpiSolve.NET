using OptiSolver.NET.Core;
using OptiSolver.NET.Exceptions;
using OptiSolver.NET.Services.Base;
using System.Reflection;
using System.Text;
using System;
using System.Linq;
using System.Collections.Generic;

namespace OptiSolver.NET.Services.BranchAndBound
{
    /// <summary>
    /// Branch & Bound solver specialized for 0-1 Knapsack.
    /// - Sorts by value/weight ratio.
    /// - Uses fractional-knapsack upper bound.
    /// - DFS with pruning by bound and capacity.
    /// Returns a SolutionResult with x in original variable order.
    /// </summary>
    public sealed class BranchBoundKnapsackSolver : SolverBase
    {
        public override string AlgorithmName => "Branch and Bound Knapsack";
        public override string Description =>
            "Specialized 0–1 knapsack Branch & Bound with fractional bound and ratio sorting.";

        // Stats
        private int _nodeCount;
        private int _explored;
        private int _fathomed;
        private StringBuilder _iterationLog;

        #region Options
        public override Dictionary<string, object> GetDefaultOptions() => new()
        {
            { "MaxIterations", 250_000 },
            { "Tolerance", 1e-9 },
            { "Verbose", false },
            { "TimeLimit", 60.0 },
            { "AllowNegativeValues", false }
        };
        #endregion

        #region Public surface
        public override bool CanSolve(LPModel model)
        {
            // Classic 0-1 knapsack structure:
            // - One constraint with relation <= (capacity)
            // - All decision variables are Binary
            // - Objective is linear (values), constraint coefficients are weights
            if (model == null)
                return false;

            // Must have variables and a non-null constraints list
            if (model.Variables == null || model.Variables.Count == 0)
                return false;
            if (model.Constraints == null || model.Constraints.Count == 0)
                return false;
            if (model.Constraints.Count != 1)
                return false;

            // All binary?
            try
            {
                for (int i = 0; i < model.Variables.Count; i++)
                {
                    var v = model.Variables[i];
                    if (v.Type != VariableType.Binary)
                        return false;
                }
            }
            catch
            {
                // If the above throws because Type is absent, fail fast.
                return false;
            }

            // Relation must be <= (we accept '=' too if capacity is exact)
            var rel = GetRelation(model.Constraints[0]);
            if (rel != "<=" && rel != "=")
                return false;

            // Sanity check: dimension match between objective and constraint row
            var values = GetObjectiveCoefficients(model);
            var weights = GetConstraintCoefficients(model.Constraints[0]);
            return values != null && weights != null && values.Length == weights.Length;
        }

        public override SolutionResult Solve(LPModel model, Dictionary<string, object> options = null)
        {
            var start = DateTime.UtcNow;
            var mergedOptions = MergeOptions(options);

            // Validate model
            var validation = ValidateModel(model);
            if (validation != null)
                return validation;

            if (!CanSolve(model))
                return SolutionResult.CreateError(AlgorithmName, "Model is not a 0–1 knapsack instance.");

            _nodeCount = _explored = _fathomed = 0;
            _iterationLog = new StringBuilder();
            LogDebug("Starting Branch&Bound Knapsack", mergedOptions);
            _iterationLog.AppendLine("=== BRANCH & BOUND 0–1 KNAPSACK ===");

            try
            {
                // Extract instance
                var items = ExtractKnapsackItems(model, mergedOptions);

                var capacity = GetCapacity(model.Constraints[0]);
                if (double.IsNaN(capacity) || double.IsInfinity(capacity) || capacity < 0)
                    throw new InvalidInputException($"Invalid capacity RHS: {capacity}");

                // Sort items by ratio (v/w) descending, keep mapping to original indices
                var order = items
                    .Select((it, i) => new { it, i })
                    .OrderByDescending(x => x.it.Ratio)
                    .ToArray();

                var sortedItems = order.Select(x => x.it).ToArray();
                var sortedToOrig = order.Select(x => x.i).ToArray();

                // DFS stack
                var bestValue = 0.0;
                var bestTake = new bool[items.Length];
                var root = new KnapsackNode(items.Length) { NodeId = ++_nodeCount, Depth = 0, NextItem = 0 };
                root.Bound = FractionalBound(root, sortedItems, capacity);
                var stack = new Stack<KnapsackNode>();
                stack.Push(root);

                var maxNodes = GetOption(mergedOptions, "MaxIterations", 250_000);
                var timeLimit = GetOption(mergedOptions, "TimeLimit", 60.0);
                var tol = GetOption(mergedOptions, "Tolerance", 1e-9);

                while (stack.Count > 0)
                {
                    if (_nodeCount > maxNodes)
                    {
                        _iterationLog.AppendLine($"[STOP] Node cap reached at ~{_nodeCount}.");
                        break;
                    }
                    if ((DateTime.UtcNow - start).TotalSeconds > timeLimit)
                    {
                        _iterationLog.AppendLine($"[STOP] Time limit {timeLimit}s reached.");
                        break;
                    }

                    var node = stack.Pop();
                    _explored++;

                    // Prune by bound
                    if (node.Bound <= bestValue + tol)
                    {
                        _fathomed++;
                        continue;
                    }

                    // If all items decided, try update incumbent
                    if (node.NextItem >= sortedItems.Length)
                    {
                        if (node.Value > bestValue + tol)
                        {
                            bestValue = node.Value;
                            Array.Copy(node.Take, bestTake, bestTake.Length);
                            _iterationLog.AppendLine(
                                $"[INCUMBENT] value={bestValue:F3} weight={node.Weight:F3} depth={node.Depth}");
                        }
                        _fathomed++;
                        continue;
                    }

                    int j = node.NextItem;
                    var item = sortedItems[j];

                    // Branch 1: take item if weight allows
                    if (node.Weight + item.Weight <= capacity + tol)
                    {
                        var take = node.CloneShallow();
                        take.NodeId = ++_nodeCount;
                        take.Take[sortedToOrig[j]] = true;      // mark in original index space
                        take.Weight += item.Weight;
                        take.Value += item.Value;
                        take.NextItem = j + 1;
                        take.Depth = node.Depth + 1;
                        take.Bound = FractionalBound(take, sortedItems, capacity);

                        stack.Push(take);
                        LogNode(take, "TAKE");
                    }
                    else
                    {
                        _iterationLog.AppendLine($"[PRUNE] overweight at node #{node.NodeId} → skip TAKE");
                    }

                    // Branch 2: do not take item
                    var leave = node.CloneShallow();
                    leave.NodeId = ++_nodeCount;
                    leave.Take[sortedToOrig[j]] = false;
                    leave.NextItem = j + 1;
                    leave.Depth = node.Depth + 1;
                    leave.Bound = FractionalBound(leave, sortedItems, capacity);

                    stack.Push(leave);
                    LogNode(leave, "LEAVE");
                }

                // Build solution vector x in original order (0/1)
                var x = bestTake.Select(t => t ? 1.0 : 0.0).ToArray();
                var value = x.Select((t, i) => t * items[i].Value).Sum();

                var result = SolutionResult.CreateOptimal(
                    objectiveValue: value,
                    variableValues: x,
                    iterations: _explored,
                    algorithm: AlgorithmName,
                    solveTimeMs: (DateTime.UtcNow - start).TotalMilliseconds,
                    message: "Optimal (incumbent) found by B&B Knapsack"
                );

                // Footer summarizing best candidate for clarity in the output file
                _iterationLog.AppendLine();
                _iterationLog.AppendLine("=== BEST CANDIDATE SUMMARY (Knapsack) ===");
                _iterationLog.AppendLine($"Objective = {value:0.###}");
                _iterationLog.AppendLine($"x* = [ {string.Join(", ", x.Select(v => v.ToString("0")))} ]");

                result.Info["NodeCount"] = _nodeCount;
                result.Info["Fathomed"] = _fathomed;
                result.Info["Explored"] = _explored;
                result.Info["Capacity"] = capacity;

                // Standardize: expose composed log under IterationLog (preferred) and Log (compat)
                var composed = _iterationLog.ToString();
                result.Info["IterationLog"] = composed;
                result.Info["Log"] = composed;

                // For consistency with the rest of the project:
                result.AlgorithmUsed = AlgorithmName;

                return result;
            }
            catch (InvalidInputException iie)
            {
                return SolutionResult.CreateError(AlgorithmName, $"Invalid knapsack input: {iie.Message}");
            }
            catch (Exception ex)
            {
                return SolutionResult.CreateError(AlgorithmName, $"Unexpected error: {ex.Message}");
            }
        }
        #endregion

        #region Core helpers
        private void LogNode(KnapsackNode n, string branch)
        {
            _iterationLog.AppendLine(
                $"[#{n.NodeId}] {branch} depth={n.Depth} next={n.NextItem} val={n.Value:F3} wt={n.Weight:F3} bound={n.Bound:F3}");
        }

        private static double FractionalBound(KnapsackNode node, KnapsackItem[] sortedItems, double capacity)
        {
            // Standard fractional knapsack bound from the next undecided item onward.
            double remaining = capacity - node.Weight;
            if (remaining <= 0)
                return node.Value;

            double bound = node.Value;
            for (int k = node.NextItem; k < sortedItems.Length; k++)
            {
                var it = sortedItems[k];
                if (it.Weight <= remaining)
                {
                    remaining -= it.Weight;
                    bound += it.Value;
                }
                else
                {
                    // take fraction
                    if (it.Weight > 0)
                        bound += it.Value * (remaining / it.Weight);
                    break;
                }
            }
            return bound;
        }
        #endregion

        #region Extraction (robust to small LPModel shape differences)
        /// <summary>
        /// Builds item list from LPModel:
        /// - Values from objective coefficients (assumed maximization or interpreted as value magnitudes).
        /// - Weights from the single ≤ (or =) constraint's coefficient row.
        /// - Capacity from the same constraint RHS.
        /// Validates binary var types and dimension consistency.
        /// Throws InvalidInputException when malformed.
        /// </summary>
        private static KnapsackItem[] ExtractKnapsackItems(LPModel model, Dictionary<string, object> options)
        {
            // Determine objective sense (try to read model.ObjectiveSense if present)
            bool isMax = true;
            var senseProp = model.GetType().GetProperty("ObjectiveSense");
            if (senseProp != null)
            {
                var senseVal = senseProp.GetValue(model)?.ToString()?.Trim().ToLowerInvariant();
                if (senseVal == "min" || senseVal == "minimize" || senseVal == "minimization")
                    isMax = false;
            }

            var values = GetObjectiveCoefficients(model)
                         ?? throw new InvalidInputException("Objective coefficients not found.");
            var row = model.Constraints?[0]
                      ?? throw new InvalidInputException("Missing knapsack constraint.");
            var weights = GetConstraintCoefficients(row)
                          ?? throw new InvalidInputException("Constraint coefficients not found.");

            if (!isMax)
            {
                // Convert to equivalent maximization for knapsack
                values = values.Select(c => -c).ToArray();
            }

            if (values.Length != weights.Length)
                throw new InvalidInputException($"Objective length ({values.Length}) differs from weight row length ({weights.Length}).");

            var allowNegVal = options != null &&
                              options.TryGetValue("AllowNegativeValues", out var anv) &&
                              anv is bool b && b;

            var items = new List<KnapsackItem>(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                var v = values[i];
                var w = weights[i];

                if (w < 0)
                    throw new InvalidInputException($"Weight coefficient at index {i} is negative ({w}).");

                if (!allowNegVal && v < 0)
                    throw new InvalidInputException($"Negative value at index {i} ({v}). Enable AllowNegativeValues to permit.");

                items.Add(new KnapsackItem(i, v, w));
            }

            return items.ToArray();
        }

        private static double GetCapacity(object constraint)
        {
            // Try common names: RightHandSide, RHS, B
            var t = constraint.GetType();
            var rhsProp = t.GetProperty("RightHandSide") ?? t.GetProperty("RHS");
            if (rhsProp != null && rhsProp.PropertyType == typeof(double))
                return (double)rhsProp.GetValue(constraint);

            // Some models store RHS in a field named "B"
            var bField = t.GetField("B", BindingFlags.Public | BindingFlags.Instance);
            if (bField != null && bField.FieldType == typeof(double))
                return (double)bField.GetValue(constraint);

            throw new InvalidInputException("Could not read capacity (RHS) from the constraint.");
        }

        private static string GetRelation(object constraint)
        {
            // Property 'Relation' as enum/string; accept <= or =
            var t = constraint.GetType();
            var relProp = t.GetProperty("Relation");
            if (relProp == null)
                return "<="; // be permissive if absent

            var val = relProp.GetValue(constraint);
            if (val == null)
                return "<=";

            // Accept enum or string
            if (val is string s)
                return s.Trim();
            var toStr = val.ToString()?.Trim();
            return string.IsNullOrWhiteSpace(toStr) ? "<=" : toStr;
        }

        private static double[] GetObjectiveCoefficients(LPModel model)
        {
            // Try: model.Objective.Coefficients
            var objProp = model.GetType().GetProperty("Objective");
            if (objProp != null)
            {
                var objVal = objProp.GetValue(model);
                if (objVal != null)
                {
                    var coefProp = objVal.GetType().GetProperty("Coefficients");
                    if (coefProp != null && typeof(IEnumerable<double>).IsAssignableFrom(coefProp.PropertyType))
                        return ((IEnumerable<double>)coefProp.GetValue(objVal)).ToArray();
                }
            }

            // Try: model.ObjectiveCoefficients
            var oc = model.GetType().GetProperty("ObjectiveCoefficients");
            if (oc != null && typeof(IEnumerable<double>).IsAssignableFrom(oc.PropertyType))
                return ((IEnumerable<double>)oc.GetValue(model)).ToArray();

            // Try: model.C (common in matrix forms)
            var cProp = model.GetType().GetProperty("C");
            if (cProp != null && typeof(IEnumerable<double>).IsAssignableFrom(cProp.PropertyType))
                return ((IEnumerable<double>)cProp.GetValue(model)).ToArray();

            return null;
        }

        private static double[] GetConstraintCoefficients(object constraint)
        {
            // Try: constraint.Coefficients
            var t = constraint.GetType();
            var coefProp = t.GetProperty("Coefficients");
            if (coefProp != null && typeof(IEnumerable<double>).IsAssignableFrom(coefProp.PropertyType))
                return ((IEnumerable<double>)coefProp.GetValue(constraint)).ToArray();

            // Try: constraint.ARow
            var aRow = t.GetProperty("ARow");
            if (aRow != null && typeof(IEnumerable<double>).IsAssignableFrom(aRow.PropertyType))
                return ((IEnumerable<double>)aRow.GetValue(constraint)).ToArray();

            // Try: constraint.A (vector)
            var a = t.GetProperty("A");
            if (a != null && typeof(IEnumerable<double>).IsAssignableFrom(a.PropertyType))
                return ((IEnumerable<double>)a.GetValue(constraint)).ToArray();

            return null;
        }
        #endregion
    }
}
