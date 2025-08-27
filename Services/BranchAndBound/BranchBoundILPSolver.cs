using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OptiSolver.NET.Core;
using OptiSolver.NET.Services.Base;
using OptiSolver.NET.Services.Simplex;

namespace OptiSolver.NET.Services.BranchAndBound
{
    /// <summary>
    /// Branch & Bound for mixed/pure integer programs (MILP with integer vars).
    /// LP relaxations can be solved by Revised Simplex (default) or Primal Simplex (Tableau)
    /// based on options["BBRelaxationEngine"] = "revised" | "tableau".
    /// - Branch variable selection: fractional part closest to 0.5; tie -> lowest index.
    /// - Binary branch: x = 0 and x = 1
    /// - General integer branch: x <= floor(x*) and x >= ceil(x*)
    /// - DFS search with bound-based pruning for max/min objectives.
    /// </summary>
    public sealed class BranchBoundILPSolver : SolverBase
    {
        public override string AlgorithmName => "Branch & Bound (MILP-Integer)";
        public override string Description =>
            "Depth-first Branch & Bound for mixed/pure integer programs using LP relaxations.";

        #region Options
        public override Dictionary<string, object> GetDefaultOptions() => new()
        {
            { "MaxNodes", 100_000 },
            { "TimeLimit", 60.0 },
            { "Verbose", false },
            { "BranchRule", "closest-0.5" },
            { "PreferOnesFirst", false },
            // NEW: choose which engine solves the LP relaxations in nodes ("revised" | "tableau")
            { "BBRelaxationEngine", "revised" }
        };
        #endregion

        public override bool CanSolve(LPModel model)
        {
            if (!base.CanSolve(model))
                return false;

            // Must have at least one discrete var (Binary or Integer). Continuous vars are allowed (mixed).
            bool hasDiscrete = model.Variables.Any(v => v.Type == VariableType.Binary || v.Type == VariableType.Integer);
            return hasDiscrete;
        }

        public override SolutionResult Solve(LPModel model, Dictionary<string, object> options = null)
        {
            var started = DateTime.UtcNow;
            var opt = MergeOptions(options);

            var valid = ValidateModel(model);
            if (valid != null)
                return valid;
            if (!CanSolve(model))
                return SolutionResult.CreateError(AlgorithmName, "Model has no integer/binary variables (not an IP/MIP).");

            // Root model
            var rootModel = CloneModel(model);

            // Select LP relaxation engine
            string engine = GetOption(opt, "BBRelaxationEngine", "revised")?.ToString()?.ToLowerInvariant();
            bool useRevised = engine != "tableau";

            // LP relaxation solver instances
            var revised = useRevised ? new RevisedSimplexSolver() : null;
            var tableau = useRevised ? null : new PrimalSimplexTableauSolver();

            var isMax = model.ObjectiveType == ObjectiveType.Maximize;

            // Incumbent
            double incumbentVal = isMax ? double.NegativeInfinity : double.PositiveInfinity;
            double[] incumbentX = null;

            int explored = 0, fathomed = 0, branched = 0;

            // Composed B&B log (with every subproblem's canonical + iterations)
            var log = new StringBuilder();
            log.AppendLine("=== BRANCH & BOUND (MILP) over LP RELAXATIONS ===");
            log.AppendLine($"Objective sense        : {(isMax ? "Maximize" : "Minimize")}");
            log.AppendLine($"Relaxation engine used : {(useRevised ? "Revised Simplex (product form & price-out)" : "Primal Simplex (Tableau)")}");
            log.AppendLine();

            // DFS stack and Node ID management
            var stack = new Stack<Node>();
            var root = new Node { NodeId = 1, Depth = 0 };
            stack.Push(root);
            int nextNodeId = 1;

            int maxNodes = GetOption(opt, "MaxNodes", 100_000);
            double timeLimit = GetOption(opt, "TimeLimit", 60.0);
            bool onesFirst = GetOption(opt, "PreferOnesFirst", false);

            while (stack.Count > 0)
            {
                if (explored >= maxNodes)
                { log.AppendLine("[STOP] MaxNodes reached."); break; }
                if ((DateTime.UtcNow - started).TotalSeconds > timeLimit)
                { log.AppendLine("[STOP] TimeLimit reached."); break; }

                var node = stack.Pop();
                explored++;

                // Build node model with bound tightenings
                var nodeModel = ApplyBounds(CloneModel(rootModel), node.Bounds);

                // Solve LP relaxation
                SolutionResult lp;
                if (useRevised)
                    lp = revised!.Solve(nodeModel);
                else
                    lp = tableau!.Solve(nodeModel);

                // --- BEGIN: attach per-node canonical & iterations from chosen engine ---
                log.AppendLine();
                log.AppendLine($"--- Subproblem Node #{node.NodeId} (depth={node.Depth}) ---");
                log.AppendLine($"Bounds: {FormatBounds(node.Bounds)}");

                if (lp != null)
                {
                    log.AppendLine($"LP status: {lp.Status}; Obj={lp.ObjectiveValue:0.###}");
                    if (lp.Info != null && lp.Info.TryGetValue("IterationLog", out var iterObj) &&
                        iterObj is string lpLog && !string.IsNullOrWhiteSpace(lpLog))
                    {
                        // Revised/Tableau log already contains canonical header + iterations
                        log.AppendLine(lpLog);
                    }
                    else
                    {
                        log.AppendLine("(No iteration log returned from LP relaxation.)");
                    }
                }
                else
                {
                    log.AppendLine("(LP solver returned null result.)");
                }
                log.AppendLine($"--- End Node #{node.NodeId} ---");
                // --- END: attach per-node canonical & iterations ---

                if (lp == null || lp.IsInfeasible)
                { fathomed++; continue; }

                if (lp.IsUnbounded)
                {
                    // Relaxation unbounded: conservatively fathom.
                    fathomed++;
                    continue;
                }
                if (!lp.IsOptimal)
                { fathomed++; continue; }

                double bound = lp.ObjectiveValue;

                // Pruning by bound vs incumbent
                if (isMax)
                {
                    if (bound <= incumbentVal + 1e-9)
                    { fathomed++; continue; }
                }
                else
                {
                    // minimization: LP relaxation is a lower bound
                    if (bound >= incumbentVal - 1e-9)
                    { fathomed++; continue; }
                }

                var x = lp.VariableValues;

                // If LP solution is integral in all integer/binary vars -> update incumbent
                if (IsIntegerFeasible(nodeModel, x))
                {
                    if ((isMax && bound > incumbentVal + 1e-9) || (!isMax && bound < incumbentVal - 1e-9))
                    {
                        incumbentVal = bound;
                        incumbentX = MapBackToOriginal(model, nodeModel, x);
                        log.AppendLine($"[INCUMBENT] value={incumbentVal:0.###} at node #{node.NodeId} with {node.Bounds.Count} bound(s)");
                    }
                    fathomed++;
                    continue;
                }

                // Choose branching variable: fractional part closest to 0.5 (tie -> lowest index)
                int j = SelectBranchVarClosestToHalf(nodeModel, x);
                if (j < 0)
                {
                    // Fallback: treat as integral (numerical noise)
                    if ((isMax && bound > incumbentVal + 1e-9) || (!isMax && bound < incumbentVal - 1e-9))
                    {
                        incumbentVal = bound;
                        incumbentX = MapBackToOriginal(model, nodeModel, x);
                        log.AppendLine($"[INCUMBENT*] value={incumbentVal:0.###} (fallback integral) at node #{node.NodeId}");
                    }
                    fathomed++;
                    continue;
                }

                branched++;

                // Build child nodes
                var v = nodeModel.Variables[j];
                double xj = SafeAt(x, j);
                if (v.Type == VariableType.Binary)
                {
                    // x_j = 1  and  x_j = 0
                    var child1 = node.Clone();
                    child1.NodeId = ++nextNodeId;
                    child1.Depth = node.Depth + 1;
                    TightenBinary(child1.Bounds, j, 1);

                    var child0 = node.Clone();
                    child0.NodeId = ++nextNodeId;
                    child0.Depth = node.Depth + 1;
                    TightenBinary(child0.Bounds, j, 0);

                    if (onesFirst)
                    {
                        stack.Push(child0);
                        stack.Push(child1);
                    }
                    else
                    {
                        stack.Push(child1);
                        stack.Push(child0);
                    }
                }
                else // general integer
                {
                    // Branch into: x_j <= floor(xj), and x_j >= ceil(xj)
                    int floorVal = (int)Math.Floor(xj);
                    int ceilVal = (int)Math.Ceiling(xj);

                    var left = node.Clone();   // x_j <= floorVal
                    left.NodeId = ++nextNodeId;
                    left.Depth = node.Depth + 1;
                    TightenUpper(left.Bounds, j, floorVal);

                    var right = node.Clone();  // x_j >= ceilVal
                    right.NodeId = ++nextNodeId;
                    right.Depth = node.Depth + 1;
                    TightenLower(right.Bounds, j, ceilVal);

                    // Push right then left so the left (<= floor) is processed first (DFS)
                    stack.Push(right);
                    stack.Push(left);
                }
            }

            // Best candidate summary footer
            log.AppendLine();
            log.AppendLine("=== BEST CANDIDATE SUMMARY ===");
            if (incumbentX != null)
            {
                log.AppendLine($"Incumbent objective = {incumbentVal:0.###}");
                log.AppendLine($"x* = [ {string.Join(", ", incumbentX.Select(v => v.ToString("0.###")))} ]");
            }
            else
            {
                log.AppendLine("No integer-feasible incumbent found.");
            }

            var res = (incumbentX != null)
                ? SolutionResult.CreateOptimal(
                    objectiveValue: incumbentVal,
                    variableValues: incumbentX,
                    iterations: explored,
                    algorithm: AlgorithmName,
                    solveTimeMs: (DateTime.UtcNow - started).TotalMilliseconds,
                    message: "Optimal (incumbent) via Branch & Bound"
                  )
                : SolutionResult.CreateInfeasible(AlgorithmName, "No feasible integer solution found.");

            res.Info["Explored"] = explored;
            res.Info["Fathomed"] = fathomed;
            res.Info["Branched"] = branched;

            // Standardize: expose the composed B&B log (with all subproblem canonical+iterations)
            var composed = log.ToString();
            res.Info["IterationLog"] = composed; // preferred by OutputWriter
            res.Info["Log"] = composed;          // kept for compatibility

            return res;
        }

        #region Internal helpers

        private sealed class Node
        {
            // List of (varIndex, newLower, newUpper) bound tightenings relative to root model
            public List<(int idx, double? lb, double? ub)> Bounds { get; } = new();

            // For logging clarity
            public int NodeId { get; set; }
            public int Depth { get; set; }

            public Node Clone()
            {
                var n = new Node { NodeId = this.NodeId, Depth = this.Depth };
                n.Bounds.AddRange(Bounds);
                return n;
            }
        }

        private static double SafeAt(double[] arr, int i) => (arr != null && i >= 0 && i < arr.Length) ? arr[i] : 0.0;

        private static string FormatBounds(List<(int idx, double? lb, double? ub)> bounds)
        {
            if (bounds == null || bounds.Count == 0)
                return "(none)";
            var parts = bounds.Select(b =>
            {
                var segs = new List<string>();
                if (b.lb.HasValue)
                    segs.Add($"x{b.idx + 1}>= {b.lb.Value:0.###}");
                if (b.ub.HasValue)
                    segs.Add($"x{b.idx + 1}<= {b.ub.Value:0.###}");
                return string.Join(" & ", segs);
            });
            return string.Join("; ", parts);
        }

        private static bool IsIntegerFeasible(LPModel m, double[] x)
        {
            const double tol = 1e-7;
            for (int i = 0; i < m.Variables.Count; i++)
            {
                var v = m.Variables[i];
                if (v.Type == VariableType.Binary || v.Type == VariableType.Integer)
                {
                    double xi = SafeAt(x, i);
                    if (Math.Abs(xi - Math.Round(xi)) > tol)
                        return false;

                    if (v.Type == VariableType.Binary)
                    {
                        var r = Math.Round(xi);
                        if (r != 0.0 && r != 1.0)
                            return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Pick integer/binary var whose fractional part is closest to 0.5.
        /// Ties broken by lowest index.
        /// Only considers variables with a strictly fractional value (beyond tolerance).
        /// </summary>
        private static int SelectBranchVarClosestToHalf(LPModel m, double[] x)
        {
            const double tol = 1e-7;
            int bestIdx = -1;
            double bestScore = double.NegativeInfinity; // higher is better

            for (int i = 0; i < m.Variables.Count; i++)
            {
                var v = m.Variables[i];
                if (v.Type != VariableType.Binary && v.Type != VariableType.Integer)
                    continue;

                double xi = SafeAt(x, i);
                double frac = Math.Abs(xi - Math.Floor(xi)); // in [0,1)
                if (frac < tol || 1.0 - frac < tol)
                    continue; // effectively integral

                double score = 0.5 - Math.Abs(xi - 0.5); // larger is closer to 0.5

                if (score > bestScore + 1e-15 || (Math.Abs(score - bestScore) <= 1e-15 && (bestIdx == -1 || i < bestIdx)))
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        private static void TightenBinary(List<(int idx, double? lb, double? ub)> bounds, int j, int val)
        {
            TightenLower(bounds, j, val);
            TightenUpper(bounds, j, val);
        }

        private static void TightenLower(List<(int idx, double? lb, double? ub)> bounds, int j, double newLb)
        {
            for (int k = 0; k < bounds.Count; k++)
            {
                var (idx, lb, ub) = bounds[k];
                if (idx == j)
                {
                    bounds[k] = (idx, (lb.HasValue ? Math.Max(lb.Value, newLb) : newLb), ub);
                    return;
                }
            }
            bounds.Add((j, newLb, null));
        }

        private static void TightenUpper(List<(int idx, double? lb, double? ub)> bounds, int j, double newUb)
        {
            for (int k = 0; k < bounds.Count; k++)
            {
                var (idx, lb, ub) = bounds[k];
                if (idx == j)
                {
                    bounds[k] = (idx, lb, (ub.HasValue ? Math.Min(ub.Value, newUb) : newUb));
                    return;
                }
            }
            bounds.Add((j, null, newUb));
        }

        private static LPModel ApplyBounds(LPModel model, List<(int idx, double? lb, double? ub)> bounds)
        {
            foreach (var (idx, lb, ub) in bounds)
            {
                if (idx < 0 || idx >= model.Variables.Count)
                    continue;
                var v = model.Variables[idx];
                if (lb.HasValue)
                    v.LowerBound = Math.Max(v.LowerBound, lb.Value);
                if (ub.HasValue)
                    v.UpperBound = Math.Min(v.UpperBound, ub.Value);
                // If infeasible (lb > ub), relaxation will detect infeasibility during solve.
            }
            return model;
        }

        private static LPModel CloneModel(LPModel m)
        {
            var copy = new LPModel
            {
                ObjectiveType = m.ObjectiveType
            };

            // Variables (preserve original bounds/types/coefficients)
            foreach (var v in m.Variables)
            {
                var nv = new Variable(
                    index: v.Index,
                    name: v.Name,
                    coefficient: v.Coefficient,
                    type: v.Type,
                    lowerBound: v.LowerBound,
                    upperBound: v.UpperBound
                );
                copy.Variables.Add(nv);
            }

            // Constraints
            foreach (var c in m.Constraints)
            {
                var nc = new Constraint
                {
                    Relation = c.Relation,
                    RightHandSide = c.RightHandSide
                };
                foreach (var coef in c.Coefficients)
                    nc.Coefficients.Add(coef);
                copy.Constraints.Add(nc);
            }

            return copy;
        }

        /// <summary>
        /// Map node-model solution (same variable order) back to original model space.
        /// (Currently aligned 1:1; kept for symmetry/future transformations.)
        /// </summary>
        private static double[] MapBackToOriginal(LPModel original, LPModel nodeModel, double[] xNode)
        {
            int n = original.Variables.Count;
            var x = new double[n];
            for (int i = 0; i < n && i < xNode.Length; i++)
                x[i] = xNode[i];
            return x;
        }

        #endregion
    }
}
