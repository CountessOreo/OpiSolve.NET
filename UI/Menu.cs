using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using OptiSolver.NET.Controller;
using OptiSolver.NET.Services.Base;
using OptiSolver.NET.Core;
using OptiSolver.NET.Analysis;   // sensitivity/duality
using OptiSolver.NET.Services.BranchAndBound; // for CanSolve guardrail on knapsack

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
                if (!System.IO.File.Exists(path))
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
                    Console.WriteLine("  1) Primal Simplex (Tableau)");
                    Console.WriteLine("  2) Revised Simplex (Two-Phase)   [default]");
                    Console.WriteLine("  3) Branch & Bound 0-1 Knapsack   (single ≤ constraint + binary)");
                    Console.WriteLine("  4) Branch & Bound (Simplex)      [not available yet]");
                    Console.WriteLine("  5) Cutting Plane (Gomory)        [not available yet]");
                    Console.Write("Selection [1-5 or ?]: ");
                    var choice = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();

                    if (choice == "?")
                    {
                        Console.WriteLine();
                        Console.WriteLine(OptiSolver.NET.IO.InputParser.GetSampleFormat());
                        Console.WriteLine();
                        continue; // re-prompt selection
                    }

                    if (choice == "4" || choice == "5")
                    {
                        Console.WriteLine("Selected solver is not available yet. Please choose another.\n");
                        continue; // re-prompt selection (no fallback)
                    }

                    solverKey = choice switch
                    {
                        "1" => "tableau",
                        "3" => "knapsack",
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

                Console.WriteLine();
                Console.WriteLine($"[RUN] {solverKey} on \"{path}\"");
                var controller = new SolverController();
                SolutionResult result;
                LPModel model;

                try
                {
                    // Parse model separately so we can reuse it for sensitivity
                    var parser = new OptiSolver.NET.IO.InputParser();
                    model = parser.ParseFile(path);

                    // QoL: echo detected size
                    var m = model.Constraints.Count;
                    var n = model.Variables.Count;
                    Console.WriteLine($"Detected: m = {m} constraint(s), n = {n} variable(s).");

                    // Guardrail: if user chose knapsack but the model isn't eligible, do not fallback
                    if (solverKey == "knapsack")
                    {
                        var bb = new BranchBoundKnapsackSolver();
                        if (!bb.CanSolve(model))
                        {
                            Console.WriteLine("Knapsack solver not available for this model (requires single ≤ constraint and binary variables).");
                            goto AskAgain; // skip solve; return to main menu
                        }
                    }

                    result = controller.SolveModel(model, solverKey, options);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unexpected error: {ex.Message}\n");
                    goto AskAgain;
                }

                OptiSolver.NET.IO.OutputWriter.WriteToConsole(result);

                string defaultResults = System.IO.Path.ChangeExtension(path, ".results.txt");
                string defaultLog = System.IO.Path.ChangeExtension(path, ".log.txt");

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

                // Sensitivity submenu when optimal
                if (result.IsOptimal || result.Status == SolutionStatus.AlternativeOptimal)
                {
                    Console.WriteLine();
                    Console.Write("Run sensitivity analysis? (y/N): ");
                    var runSa = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
                    if (runSa == "y" || runSa == "yes")
                    {
                        RunSensitivityMenu(model, result, path);
                    }
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
                            var report = SensitivityAnalyser.BuildReport(model, result);
                            Console.WriteLine(report);
                            MaybeSave(report, System.IO.Path.ChangeExtension(inputPath, ".sensitivity.txt"));
                            break;
                        }
                        case "2":
                        {
                            var y = ShadowPriceCalculator.FromRevisedArtifacts(result)
                                    ?? ShadowPriceCalculator.TryFromTableau(result);
                            if (y == null)
                            {
                                Console.WriteLine("Shadow prices not available (need Revised Simplex artifacts).");
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
                            var ranges = RangeAnalyser.CostRangesForNonBasic(model, result);
                            if (ranges.Count == 0)
                            { Console.WriteLine("No non-basic ranges."); break; }
                            Console.WriteLine("-- Cost Ranges (Non-Basic) --");
                            foreach (var r in ranges)
                                Console.WriteLine($"x{r.VarIndex + 1}: c in [{Fmt(r.Min)}, {Fmt(r.Max)}], Δ+={Fmt(r.MaxIncrease)}, Δ-={Fmt(r.MaxDecrease)}  (rc={Fmt(r.ReducedCost)})");
                            MaybeSave(Lines(ranges.Select(r => $"x{r.VarIndex + 1}: [{Fmt(r.Min)}, {Fmt(r.Max)}], rc={Fmt(r.ReducedCost)}")), System.IO.Path.ChangeExtension(inputPath, ".ranges.nonbasic.txt"));
                            break;
                        }
                        case "4":
                        {
                            var ranges = RangeAnalyser.CostRangesForBasic(model, result);
                            if (ranges.Count == 0)
                            { Console.WriteLine("No basic ranges."); break; }
                            Console.WriteLine("-- Cost Ranges (Basic) --");
                            foreach (var r in ranges)
                                Console.WriteLine($"x{r.VarIndex + 1}: c in [{Fmt(r.Min)}, {Fmt(r.Max)}], Δ+={Fmt(r.MaxIncrease)}, Δ-={Fmt(r.MaxDecrease)}");
                            MaybeSave(Lines(ranges.Select(r => $"x{r.VarIndex + 1}: [{Fmt(r.Min)}, {Fmt(r.Max)}]")), System.IO.Path.ChangeExtension(inputPath, ".ranges.basic.txt"));
                            break;
                        }
                        case "5":
                        {
                            var ranges = RangeAnalyser.RhsRanges(model, result);
                            if (ranges.Count == 0)
                            { Console.WriteLine("No RHS ranges."); break; }
                            Console.WriteLine("-- RHS Ranges --");
                            for (int i = 0; i < ranges.Count; i++)
                                Console.WriteLine($"b{i + 1}: b in [{Fmt(ranges[i].Min)}, {Fmt(ranges[i].Max)}], Δ+={Fmt(ranges[i].MaxIncrease)}, Δ-={Fmt(ranges[i].MaxDecrease)}");
                            MaybeSave(Lines(ranges.Select((r, i) => $"b{i + 1}: [{Fmt(r.Min)}, {Fmt(r.Max)}]")), System.IO.Path.ChangeExtension(inputPath, ".ranges.rhs.txt"));
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
                            var (xNew, objNew) = SensitivityAnalyser.ApplyRhsChange(model, result, i - 1, delta);
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
                            var (xNew, objNew) = SensitivityAnalyser.ApplyCostChange(model, result, j - 1, dc);
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
                            var (rc, profit) = SensitivityAnalyser.EvaluateNewActivity(model, result, aNew, cNew);
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
                            var (slack, feasible) = SensitivityAnalyser.EvaluateNewConstraint(model, result, aNew, bNew);
                            Console.WriteLine($"Slack = {slack:0.000}  → {(feasible ? "Feasible under current x*" : "Violates current x*")}");
                            break;
                        }
                        case "10":
                        {
                            var summary = DualityAnalyser.CheckAndSummarize(model, result);
                            Console.WriteLine(summary);
                            MaybeSave(summary, System.IO.Path.ChangeExtension(inputPath, ".duality.txt"));
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
