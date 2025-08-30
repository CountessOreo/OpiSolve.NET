using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OptiSolver.NET.Core;
using OptiSolver.NET.Services.Base;
using OptiSolver.NET.Services.Simplex;

namespace OptiSolver.NET.Services.BranchAndBound
{
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
            { "BBRelaxationEngine", "revised" } // "revised" | "tableau"
        };
        #endregion

        public override bool CanSolve(LPModel model)
        {
            if (!base.CanSolve(model))
                return false;
            return model.Variables.Any(v => v.Type == VariableType.Binary || v.Type == VariableType.Integer);
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

            var rootModel = CloneModel(model);

            string engine = GetOption(opt, "BBRelaxationEngine", "revised")?.ToString()?.ToLowerInvariant();
            bool useRevised = engine != "tableau";

            var revised = useRevised ? new RevisedSimplexSolver() : null;
            var tableau = useRevised ? null : new PrimalSimplexTableauSolver();

            bool isMax = model.ObjectiveType == ObjectiveType.Maximize;

            // Incumbent stored in RAW min-form to keep pruning consistent
            double incumbentVal = isMax ? double.NegativeInfinity : double.PositiveInfinity;
            double[] incumbentX = null;

            int explored = 0, fathomed = 0, branched = 0;

            var log = new StringBuilder();
            log.AppendLine("=== BRANCH & BOUND (MILP) over LP RELAXATIONS ===");
            log.AppendLine($"Objective sense        : {(isMax ? "Maximize" : "Minimize")}");
            log.AppendLine($"Relaxation engine used : {(useRevised ? "Revised Simplex (Two-Phase)" : "Primal Simplex (Tableau)")}");
            log.AppendLine();

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

                var nodeModel = ApplyBounds(CloneModel(rootModel), node.Bounds);

                // Solve LP relaxation
                SolutionResult lp = useRevised ? revised!.Solve(nodeModel) : tableau!.Solve(nodeModel);

                // --- per-node logging (normalized) ---
                log.AppendLine();
                log.AppendLine($"--- Subproblem Node #{node.NodeId} (depth={node.Depth}) ---");
                log.AppendLine($"Bounds: {FormatBounds(node.Bounds)}");

                double bound = lp.ObjectiveValue; // RAW min-form

                if (lp != null)
                {
                    log.AppendLine($"LP status: {lp.Status}; Obj={bound:0.###}");

                    // Prefer a full block if present; otherwise iteration log; normalize final section
                    string lpBlock = null;
                    if (lp.Info != null && lp.Info.TryGetValue("CanonicalAndIterations", out var full) && full is string sFull && !string.IsNullOrWhiteSpace(sFull))
                        lpBlock = sFull;
                    else if (lp.Info != null && lp.Info.TryGetValue("IterationLog", out var iterObj) && iterObj is string sIter && !string.IsNullOrWhiteSpace(sIter))
                        lpBlock = sIter;
                    else if (lp.Info != null && lp.Info.TryGetValue("Log", out var sLogObj) && sLogObj is string sLog && !string.IsNullOrWhiteSpace(sLog))
                        lpBlock = sLog;

                    if (!string.IsNullOrWhiteSpace(lpBlock))
                        log.AppendLine(NormalizeLpLogToUserSense(lpBlock, model.ObjectiveType, lp));
                    else
                        log.AppendLine("(No iteration log returned from LP relaxation.)");
                }
                else
                {
                    log.AppendLine("(LP solver returned null result.)");
                }
                log.AppendLine($"--- End Node #{node.NodeId} ---");
                // ------------------------------------

                if (lp == null || lp.IsInfeasible)
                { fathomed++; continue; }
                if (lp.IsUnbounded)
                { fathomed++; continue; }
                if (!lp.IsOptimal)
                { fathomed++; continue; }

                // Prune by bound (RAW)
                if (isMax)
                {
                    if (bound <= incumbentVal + 1e-9)
                    { fathomed++; continue; } // LP upper bound not better
                }
                else
                {
                    if (bound >= incumbentVal - 1e-9)
                    { fathomed++; continue; } // LP lower bound not better
                }

                var x = lp.VariableValues;

                // Integral?
                if (IsIntegerFeasible(nodeModel, x))
                {
                    if ((isMax && bound > incumbentVal + 1e-9) || (!isMax && bound < incumbentVal - 1e-9))
                    {
                        incumbentVal = bound;   // user-sense
                        incumbentX = MapBackToOriginal(model, nodeModel, x);
                        log.AppendLine($"[INCUMBENT] value={incumbentVal:0.###} at node #{node.NodeId} with {node.Bounds.Count} bound(s)");
                    }
                    fathomed++;
                    continue;
                }

                // Branch: fractional closest to 0.5
                int j = SelectBranchVarClosestToHalf(nodeModel, x);
                if (j < 0)
                {
                    if ((isMax && bound > incumbentVal + 1e-9) || (!isMax && bound < incumbentVal - 1e-9))
                    {
                        incumbentVal = bound;   // user-sense
                        incumbentX = MapBackToOriginal(model, nodeModel, lp.VariableValues);
                        log.AppendLine($"[INCUMBENT] value={incumbentVal:0.###} at node #{node.NodeId} with {node.Bounds.Count} bound(s)");
                    }
                    fathomed++;
                    continue;
                }

                branched++;

                var v = nodeModel.Variables[j];
                double xj = SafeAt(x, j);

                if (v.Type == VariableType.Binary)
                {
                    var child1 = node.Clone();
                    child1.NodeId = ++nextNodeId;
                    child1.Depth = node.Depth + 1;
                    TightenBinary(child1.Bounds, j, 1);

                    var child0 = node.Clone();
                    child0.NodeId = ++nextNodeId;
                    child0.Depth = node.Depth + 1;
                    TightenBinary(child0.Bounds, j, 0);

                    if (onesFirst)
                    { stack.Push(child0); stack.Push(child1); }
                    else
                    { stack.Push(child1); stack.Push(child0); }
                }
                else
                {
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

                    stack.Push(right);
                    stack.Push(left);
                }
            }

            // Footer (user-sense)
            log.AppendLine();
            log.AppendLine("=== BEST CANDIDATE SUMMARY ===");
            if (incumbentX != null)
            {
                log.AppendLine($"Incumbent objective = {incumbentVal:0.###}");
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

            // Let the writers know the sense + style/engine
            res.ObjectiveSense = model.ObjectiveType;
            res.Info["RelaxationEngine"] = useRevised ? "revised" : "tableau";
            res.Info["Style"] = useRevised
                ? "Branch & Bound over Revised Simplex relaxations"
                : "Branch & Bound over Tableau relaxations";
            res.Info["Explored"] = explored;
            res.Info["Fathomed"] = fathomed;
            res.Info["Branched"] = branched;

            var composed = log.ToString();
            res.Info["CanonicalAndIterations"] = composed; // preferred by OutputWriter
            // (Optional legacy keys)
            res.Info["IterationLog"] = composed;
            res.Info["Log"] = composed;

            return res;
        }

        // ---------- helpers to normalize LP logs ----------
        private static string NormalizeLpLogToUserSense(string lpLog, ObjectiveType sense, SolutionResult lp)
        {
            if (string.IsNullOrWhiteSpace(lpLog))
                return lpLog ?? string.Empty;

            // Strip any prior "FINAL SOLUTION:" block (if present) and re-append a clean final block.
            var idx = lpLog.IndexOf("FINAL SOLUTION:", StringComparison.OrdinalIgnoreCase);
            string head = idx >= 0 ? lpLog.Substring(0, idx).TrimEnd() : lpLog.TrimEnd();

            var sb = new StringBuilder();
            sb.AppendLine(head);
            sb.AppendLine();
            sb.AppendLine("FINAL SOLUTION:");

            // User-sense directly from the LP solver:
            double user = lp.ObjectiveValue;

            sb.AppendLine($"Objective Value: {user:0,0.###}");
            sb.AppendLine($"Iterations: {lp.Iterations}");
            if (lp.VariableValues != null && lp.VariableValues.Length > 0)
                sb.AppendLine($"Variables: [ {string.Join(", ", lp.VariableValues.Select(v => v.ToString("0.000")))} ]");

            return sb.ToString();
        }

        #region Internal helpers

        private sealed class Node
        {
            public List<(int idx, double? lb, double? ub)> Bounds { get; } = new();
            public int NodeId { get; set; }
            public int Depth { get; set; }
            public Node Clone()
            {
                var n = new Node { NodeId = this.NodeId, Depth = this.Depth };
                n.Bounds.AddRange(Bounds);
                return n;
            }
        }

        private static double SafeAt(double[] arr, int i) =>
            (arr != null && i >= 0 && i < arr.Length) ? arr[i] : 0.0;

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

        private static int SelectBranchVarClosestToHalf(LPModel m, double[] x)
        {
            const double tol = 1e-7;
            int bestIdx = -1;
            double bestScore = double.NegativeInfinity;

            for (int i = 0; i < m.Variables.Count; i++)
            {
                var v = m.Variables[i];
                if (v.Type != VariableType.Binary && v.Type != VariableType.Integer)
                    continue;

                double xi = SafeAt(x, i);
                double frac = Math.Abs(xi - Math.Floor(xi)); // [0,1)
                if (frac < tol || 1.0 - frac < tol)
                    continue;

                double score = 0.5 - Math.Abs(xi - 0.5); // closer to 0.5 is better
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
            }
            return model;
        }

        private static LPModel CloneModel(LPModel m)
        {
            var copy = new LPModel { ObjectiveType = m.ObjectiveType };
            foreach (var v in m.Variables)
            {
                var nv = new Variable(v.Index, v.Name, v.Coefficient, v.Type, v.LowerBound, v.UpperBound);
                copy.Variables.Add(nv);
            }
            foreach (var c in m.Constraints)
            {
                var nc = new Constraint { Relation = c.Relation, RightHandSide = c.RightHandSide };
                foreach (var coef in c.Coefficients)
                    nc.Coefficients.Add(coef);
                copy.Constraints.Add(nc);
            }
            return copy;
        }

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
