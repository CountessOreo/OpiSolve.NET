using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using OptiSolver.NET.Controller;
using OptiSolver.NET.Services.Base;
using OptiSolver.NET.Core;
using OptiSolver.NET.Analysis;   // sensitivity/duality
using OptiSolver.NET.Services.BranchAndBound; // for knapsack CanSolve guardrail
using OptiSolver.NET.Services.Simplex;        // RevisedSimplexSolver for LP relaxation

namespace OptiSolver.NET.UI
{
    internal static class Menu
    {
        /// <summary>
        /// Console UI for the required "menu driven" executable.
        /// </summary>
        public static void Run()
        {
            while (true)
            {
                Console.WriteLine("=== OptiSolver.NET — Linear & Integer Programming ===");
                Console.WriteLine("Input format: see InputParser comments (objective; constraints; types).");
                Console.WriteLine("Type '?' at the solver prompt for a sample format.\n");

                Console.Write("Enter path to input file: ");
                var path = (Console.ReadLine() ?? "").Trim();
                if (string.IsNullOrWhiteSpace(path))
                {
                    Console.WriteLine("No path given. Exiting.");
                    return;
                }
                if (!File.Exists(path))
                {
                    Console.WriteLine($"File not found: {path}\n");
                    continue; // ask again
                }

                // ========= 2) Solver selection (with '?' help + guardrails) =========
                string solverKey = null;
                while (true)
                {
                    Console.WriteLine();
                    Console.WriteLine("Choose solver:");
                    Console.WriteLine("  1) Primal Simplex ");
                    Console.WriteLine("  2) Revised Simplex (Two-Phase) ");
                    Console.WriteLine("  3) Branch & Bound 0-1 Knapsack ");
                    Console.WriteLine("  4) Branch & Bound (Simplex, Mixed/Pure Integer)");
                    Console.WriteLine("  5) Cutting Plane (Gomory)");
                    Console.WriteLine("  6) Nonlinear ");
                    Console.Write("Selection [1-6 or ?]: ");
                    var choice = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();

                    if (choice == "?")
                    {
                        Console.WriteLine();
                        Console.WriteLine(OptiSolver.NET.IO.InputParser.GetSampleFormat());
                        Console.WriteLine();
                        continue; // re-prompt selection
                    }

                    solverKey = choice switch
                    {
                        "1" => "tableau",
                        "3" => "knapsack",
                        "4" => "bb-ilp",
                        "5" => "cutting",
                        "6" => "nonlinear-demo",
                        _ => "revised",
                    };
                    break;
                }

                // ========= 3) Basic options =========
                var options = new Dictionary<string, object>();
                Console.Write("MaxIterations (blank=default): ");
                if (int.TryParse(Console.ReadLine(), out int mi))
                    options["MaxIterations"] = mi;

                Console.Write("Use Bland's Rule? (y/N): ");
                var bland = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
                if (bland == "y" || bland == "yes")
                    options["BlandsRule"] = true;

                Console.Write("Verbose log to console? (y/N): ");
                var verb = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
                if (verb == "y" || verb == "yes")
                    options["Verbose"] = true;

                // Optional: B&B-specific knobs (and toggle for tableau relaxations)
                if (solverKey == "bb-ilp")
                {
                    Console.Write("Use tableau relaxations for B&B subproblems? (y/N): ");
                    var tt = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
                    if (tt == "y" || tt == "yes")
                    {
                        // Flip the key; controller will inject options["BBRelaxationEngine"]="tableau"
                        solverKey = "bb-ilp-tableau";
                    }

                    Console.Write("B&B MaxNodes (blank=100000): ");
                    if (int.TryParse(Console.ReadLine(), out int maxNodes))
                        options["MaxNodes"] = maxNodes;

                    Console.Write("B&B TimeLimit seconds (blank=60): ");
                    if (double.TryParse(Console.ReadLine(), out double tl))
                        options["TimeLimit"] = tl;
                }

                if (solverKey == "cutting")
                {
                    Console.Write("MaxCuts (blank=50): ");
                    if (int.TryParse(Console.ReadLine(), out var mc))
                        options["MaxCuts"] = mc;
                    Console.Write("Tolerance (blank=1e-9): ");
                    if (double.TryParse(Console.ReadLine(), out var tol))
                        options["Tolerance"] = tol;
                }

                // Nonlinear demo: collect function & bounds
                if (solverKey == "nonlinear-demo")
                {
                    Console.Write("Enter function f(x) (e.g., x^2 + 3*x + 2): ");
                    var fx = Console.ReadLine();
                    Console.Write("Lower bound L (blank=-1e9): ");
                    var sL = Console.ReadLine();
                    Console.Write("Upper bound U (blank=+1e9): ");
                    var sU = Console.ReadLine();
                    Console.Write("Initial x0 (blank=0): ");
                    var sX0 = Console.ReadLine();

                    options["Function"] = string.IsNullOrWhiteSpace(fx) ? "x^2" : fx.Trim();
                    if (double.TryParse(sL, out var L))
                        options["LowerBound"] = L;
                    if (double.TryParse(sU, out var U))
                        options["UpperBound"] = U;
                    if (double.TryParse(sX0, out var X0))
                        options["InitialX"] = X0;

                    Console.Write("Nonlinear verbose log? (y/N): ");
                    var nv = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
                    if (nv == "y" || nv == "yes")
                        options["Verbose"] = true;
                }

                Console.WriteLine();
                Console.WriteLine($"[RUN] {solverKey} on \"{path}\"");
                var controller = new SolverController();
                SolutionResult result;
                LPModel model;

                try
                {
                    // Parse model only when needed (nonlinear demo doesn't use it)
                    if (solverKey == "nonlinear-demo")
                    {
                        model = new LPModel();
                        Console.WriteLine("Nonlinear demo selected: skipping LP parsing.");
                    }
                    else
                    {
                        var parser = new OptiSolver.NET.IO.InputParser();
                        model = parser.ParseFile(path);

                        // QoL: echo detected size
                        var m = model.Constraints.Count;
                        var n = model.Variables.Count;
                        Console.WriteLine($"Detected: m = {m} constraint(s), n = {n} variable(s).");
                    }

                    // Guardrail: knapsack eligibility
                    if (solverKey == "knapsack")
                    {
                        var bb = new BranchBoundKnapsackSolver();
                        if (!bb.CanSolve(model))
                        {
                            Console.WriteLine("Knapsack solver not available for this model (requires single ≤ constraint and binary variables).");
                            goto AskAgain;
                        }
                    }

                    result = controller.SolveModel(model, solverKey, options);

                    // Annotate sense for downstream display; do NOT flip objective value here.
                    result.ObjectiveSense = model.ObjectiveType;
                    result.Info["ObjectiveSense"] = model.ObjectiveType;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unexpected error: {ex.Message}\n");
                    goto AskAgain;
                }

                // Present & persist results
                OptiSolver.NET.IO.OutputWriter.WriteToConsole(result);

                string defaultResults = Path.ChangeExtension(path, ".results.txt");
                string defaultLog = Path.ChangeExtension(path, ".log.txt");

                OptiSolver.NET.IO.OutputWriter.WriteFullResultToFile(result, defaultResults);
                Console.WriteLine($"Full results auto-saved: {defaultResults}");

                Console.Write($"Write FULL results (canonical + all iterations) to file? (y/N) [{defaultResults}]: ");
                var writeFull = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
                if (writeFull == "y" || writeFull == "yes")
                {
                    Console.Write("Path for results file (Enter for default): ");
                    var outPath = (Console.ReadLine() ?? "").Trim();
                    outPath = string.IsNullOrWhiteSpace(outPath) ? defaultResults : outPath;
                    OptiSolver.NET.IO.OutputWriter.WriteFullResultToFile(result, outPath);
                    Console.WriteLine($"Full results written: {outPath}");
                }

                Console.Write($"Write iteration log to file? (y/N) [{defaultLog}]: ");
                var save = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
                if (save == "y" || save == "yes")
                {
                    Console.Write("Path for log file (Enter for default): ");
                    var logPath = (Console.ReadLine() ?? "").Trim();
                    logPath = string.IsNullOrWhiteSpace(logPath) ? defaultLog : logPath;
                    OptiSolver.NET.IO.OutputWriter.WriteLogToFile(result, logPath);
                    Console.WriteLine($"Log written: {logPath}");
                }

                // Sensitivity submenu when optimal (primarily for LP solves)
                if (result.IsOptimal || result.Status == SolutionStatus.AlternativeOptimal)
                {
                    Console.WriteLine();
                    Console.Write("Run sensitivity analysis? (y/N): ");
                    var runSa = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
                    if (runSa == "y" || runSa == "yes")
                        RunSensitivityMenu(model, result, path);
                }

            AskAgain:
                Console.WriteLine();
                Console.Write("Solve another model? (y/N): ");
                var again = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
                if (!(again == "y" || again == "yes"))
                    break;
                Console.Clear();
            }
        }

        // =========================
        // Sensitivity submenu
        // =========================
        private static void RunSensitivityMenu(LPModel model, SolutionResult result, string inputPath)
        {
            // Ensure we have LP sensitivity artifacts. If not (e.g., B&B or Tableau without duals),
            // solve the LP RELAXATION with REVISED SIMPLEX right now and use that for analysis.
            var saModel = model;
            var saResult = result;

            bool needsLpArtifacts = NeedsLpArtifacts(result);
            if (needsLpArtifacts)
            {
                Console.WriteLine("[info] Sensitivity needs LP artifacts (basis/duals). Solving continuous relaxation with Revised Simplex...");
                saModel = MakeContinuousRelaxation(model);
                var revised = new RevisedSimplexSolver();
                saResult = revised.Solve(saModel);
                saResult.ObjectiveSense = saModel.ObjectiveType;
                Console.WriteLine($"[info] LP relaxation: status={saResult.Status}, z={saResult.ObjectiveValue:0.###} ({saResult.ObjectiveSense})");
            }

            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("=== Sensitivity Analysis ===");
                Console.WriteLine("  1) Full report (shadow prices, ranges, duality)");
                Console.WriteLine("  2) Shadow prices only");
                Console.WriteLine("  3) Cost ranges — Non-basic variables");
                Console.WriteLine("  4) Cost ranges — Basic variables");
                Console.WriteLine("  5) RHS ranges");
                Console.WriteLine("  6) Apply Δ to RHS (keep basis)");
                Console.WriteLine("  7) Apply Δ to cost c_j (keep basis)");
                Console.WriteLine("  8) Evaluate new activity (a_new, c_new)");
                Console.WriteLine("  9) Evaluate new constraint (a_new • x ≤ b_new)");
                Console.WriteLine(" 10) Duality: build & solve dual, check strong/weak");
                Console.WriteLine("  0) Back");
                Console.Write("Select: ");
                var sel = (Console.ReadLine() ?? "").Trim();

                if (sel == "0")
                    break;

                try
                {
                    switch (sel)
                    {
                        case "1":
                        {
                            var report = SensitivityAnalyser.BuildReport(saModel, saResult);
                            Console.WriteLine(report);
                            MaybeSave(report, Path.ChangeExtension(inputPath, ".sensitivity.txt"));
                            break;
                        }
                        case "2":
                        {
                            var y = ShadowPriceCalculator.FromRevisedArtifacts(saResult)
                                    ?? ShadowPriceCalculator.TryFromTableau(saResult);
                            if (y == null)
                            {
                                Console.WriteLine("Shadow prices not available. Tip: run with Revised Simplex so B^{-1}/dual artifacts are produced.");
                            }
                            else
                            {
                                Console.WriteLine("-- Shadow Prices (π) --");
                                Console.WriteLine($"[{string.Join(", ", y.Select(v => v.ToString("0.000")))}]");
                            }
                            break;
                        }
                        case "3":
                        {
                            var ranges = RangeAnalyser.CostRangesForNonBasic(saModel, saResult);
                            if (ranges.Count == 0)
                            { Console.WriteLine("No non-basic ranges."); break; }
                            Console.WriteLine("-- Cost Ranges (Non-Basic) --");
                            foreach (var r in ranges)
                                Console.WriteLine($"x{r.VarIndex + 1}: c in [{Fmt(r.Min)}, {Fmt(r.Max)}], Δ+={Fmt(r.MaxIncrease)}, Δ-={Fmt(r.MaxDecrease)}  (rc={Fmt(r.ReducedCost)})");
                            MaybeSave(Lines(ranges.Select(r => $"x{r.VarIndex + 1}: [{Fmt(r.Min)}, {Fmt(r.Max)}], rc={Fmt(r.ReducedCost)}")), Path.ChangeExtension(inputPath, ".ranges.nonbasic.txt"));
                            break;
                        }
                        case "4":
                        {
                            var ranges = RangeAnalyser.CostRangesForBasic(saModel, saResult);
                            if (ranges.Count == 0)
                            { Console.WriteLine("No basic ranges."); break; }
                            Console.WriteLine("-- Cost Ranges (Basic) --");
                            foreach (var r in ranges)
                                Console.WriteLine($"x{r.VarIndex + 1}: c in [{Fmt(r.Min)}, {Fmt(r.Max)}], Δ+={Fmt(r.MaxIncrease)}, Δ-={Fmt(r.MaxDecrease)}");
                            MaybeSave(Lines(ranges.Select(r => $"x{r.VarIndex + 1}: [{Fmt(r.Min)}, {Fmt(r.Max)}]")), Path.ChangeExtension(inputPath, ".ranges.basic.txt"));
                            break;
                        }
                        case "5":
                        {
                            var ranges = RangeAnalyser.RhsRanges(saModel, saResult);
                            if (ranges.Count == 0)
                            { Console.WriteLine("No RHS ranges."); break; }
                            Console.WriteLine("-- RHS Ranges --");
                            for (int i = 0; i < ranges.Count; i++)
                                Console.WriteLine($"b{i + 1}: b in [{Fmt(ranges[i].Min)}, {Fmt(ranges[i].Max)}], Δ+={Fmt(ranges[i].MaxIncrease)}, Δ-={Fmt(ranges[i].MaxDecrease)}");
                            MaybeSave(Lines(ranges.Select((r, i) => $"b{i + 1}: [{Fmt(r.Min)}, {Fmt(r.Max)}]")), Path.ChangeExtension(inputPath, ".ranges.rhs.txt"));
                            break;
                        }
                        case "6":
                        {
                            Console.Write("Constraint index i (1..m): ");
                            if (!int.TryParse(Console.ReadLine(), out int i) || i <= 0)
                            { Console.WriteLine("Invalid index."); break; }
                            Console.Write("Δb_i value: ");
                            if (!double.TryParse(Console.ReadLine(), out double delta))
                            { Console.WriteLine("Invalid delta."); break; }
                            var (xNew, objNew) = SensitivityAnalyser.ApplyRhsChange(saModel, saResult, i - 1, delta);
                            Console.WriteLine($"New objective (same basis): {objNew:0.000}");
                            Console.WriteLine($"x' = [{string.Join(", ", xNew.Select(v => v.ToString("0.000")))}]");
                            break;
                        }
                        case "7":
                        {
                            Console.Write("Variable index j (1..n): ");
                            if (!int.TryParse(Console.ReadLine(), out int j) || j <= 0)
                            { Console.WriteLine("Invalid index."); break; }
                            Console.Write("Δc_j value: ");
                            if (!double.TryParse(Console.ReadLine(), out double dc))
                            { Console.WriteLine("Invalid delta."); break; }
                            var (xNew, objNew) = SensitivityAnalyser.ApplyCostChange(saModel, saResult, j - 1, dc);
                            Console.WriteLine($"New objective (same basis): {objNew:0.000}");
                            Console.WriteLine($"x' = [{string.Join(", ", xNew.Select(v => v.ToString("0.000")))}]");
                            break;
                        }
                        case "8":
                        {
                            Console.Write("m (number of constraints/rows): ");
                            if (!int.TryParse(Console.ReadLine(), out int m) || m <= 0)
                            { Console.WriteLine("Invalid m."); break; }
                            var aNew = new double[m];
                            for (int ii = 0; ii < m; ii++)
                            {
                                Console.Write($"a_new[{ii + 1}]: ");
                                if (!double.TryParse(Console.ReadLine(), out aNew[ii]))
                                { Console.WriteLine("Invalid."); return; }
                            }
                            Console.Write("c_new: ");
                            if (!double.TryParse(Console.ReadLine(), out double cNew))
                            { Console.WriteLine("Invalid."); break; }
                            var (rc, profit) = SensitivityAnalyser.EvaluateNewActivity(saModel, saResult, aNew, cNew);
                            Console.WriteLine($"Reduced cost rc = {rc:0.000}  → {(profit ? "Profitable to add" : "Not profitable")} (given current duals).");
                            break;
                        }
                        case "9":
                        {
                            Console.Write("n (number of decision variables): ");
                            if (!int.TryParse(Console.ReadLine(), out int n) || n <= 0)
                            { Console.WriteLine("Invalid n."); break; }
                            var aNew = new double[n];
                            for (int j2 = 0; j2 < n; j2++)
                            {
                                Console.Write($"a_new[{j2 + 1}]: ");
                                if (!double.TryParse(Console.ReadLine(), out aNew[j2]))
                                { Console.WriteLine("Invalid."); return; }
                            }
                            Console.Write("b_new: ");
                            if (!double.TryParse(Console.ReadLine(), out double bNew))
                            { Console.WriteLine("Invalid."); break; }
                            var (slack, feasible) = SensitivityAnalyser.EvaluateNewConstraint(saModel, saResult, aNew, bNew);
                            Console.WriteLine($"Slack = {slack:0.000}  → {(feasible ? "Feasible under current x*" : "Violates current x*")}");
                            break;
                        }
                        case "10":
                        {
                            var summary = DualityAnalyser.CheckAndSummarize(saModel, saResult);
                            Console.WriteLine(summary);
                            MaybeSave(summary, Path.ChangeExtension(inputPath, ".duality.txt"));
                            break;
                        }
                        default:
                        Console.WriteLine("Unknown selection.");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Sensitivity] Error: {ex.Message}");
                }
            }
        }

        // ------------- helpers -------------

        /// <summary>
        /// Decide whether the current result lacks LP artifacts needed for sensitivity.
        /// </summary>
        private static bool NeedsLpArtifacts(SolutionResult r)
        {
            if (r == null)
                return true;

            // Prefer Revised artifacts: BasisIndices + duals or reduced costs
            bool hasBasis = r.Info != null && r.Info.ContainsKey("BasisIndices");
            bool hasDuals = (r.DualValues != null && r.DualValues.Length > 0)
                            || (r.Info != null && (r.Info.ContainsKey("DualY") || r.Info.ContainsKey("DualValues")));
            bool hasReduced = r.ReducedCosts != null && r.ReducedCosts.Length > 0;

            // If any of these are missing, we’ll regenerate via Revised Simplex solve.
            bool isBBSolve = (r.AlgorithmUsed?.StartsWith("Branch & Bound") ?? false);
            return isBBSolve || !(hasBasis && (hasDuals || hasReduced));
        }

        /// <summary>
        /// Build the continuous relaxation: all integer/binary vars become continuous ≥ 0.
        /// Keep binary upper bounds ≤ 1.
        /// </summary>
        private static LPModel MakeContinuousRelaxation(LPModel m)
        {
            var lp = new LPModel
            {
                ObjectiveType = m.ObjectiveType,
                Name = string.IsNullOrWhiteSpace(m.Name) ? "LP Relaxation" : (m.Name + " (LP Relaxation)")
            };

            // Variables
            foreach (var v in m.Variables)
            {
                double ub = v.UpperBound;
                if (v.Type == VariableType.Binary)
                    ub = Math.Min(ub, 1.0);

                lp.Variables.Add(new Variable(
                    index: v.Index,
                    name: v.Name,
                    coefficient: v.Coefficient,
                    type: VariableType.Positive,                // continuous ≥ 0
                    lowerBound: Math.Max(0.0, v.LowerBound),
                    upperBound: double.IsInfinity(ub) ? double.PositiveInfinity : Math.Max(0.0, ub)
                ));
            }

            // Original constraints (copy as-is)
            foreach (var c in m.Constraints)
            {
                var nc = new Constraint { Relation = c.Relation, RightHandSide = c.RightHandSide };
                foreach (var aij in c.Coefficients)
                    nc.Coefficients.Add(aij);
                lp.Constraints.Add(nc);
            }

            // >>> NEW: encode finite upper bounds as explicit ≤ constraints <<<
            int n = lp.Variables.Count;
            for (int j = 0; j < n; j++)
            {
                var ub = lp.Variables[j].UpperBound;
                if (!double.IsInfinity(ub)) // include binaries (ub = 1)
                {
                    var ubRow = new Constraint
                    {
                        Relation = ConstraintRelation.LessThanOrEqual,
                        RightHandSide = ub
                    };
                    // row: e_j^T x ≤ ub
                    for (int k = 0; k < n; k++)
                        ubRow.Coefficients.Add(0.0);
                    ubRow.Coefficients[j] = 1.0;
                    lp.Constraints.Add(ubRow);
                }
            }

            return lp;
        }

        private static string Fmt(double v) => double.IsInfinity(v) ? "±∞" : v.ToString("0.000");
        private static string Lines(IEnumerable<string> lines) => string.Join(Environment.NewLine, lines);

        private static void MaybeSave(string content, string defaultPath)
        {
            Console.Write($"Save to file? (y/N) [{defaultPath}]: ");
            var s = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
            if (s == "y" || s == "yes")
            {
                Console.Write("Path (Enter for default): ");
                var p = (Console.ReadLine() ?? "").Trim();
                p = string.IsNullOrWhiteSpace(p) ? defaultPath : p;
                File.WriteAllText(p, content);
                Console.WriteLine($"Saved: {p}");
            }
        }
    }
}
