using System;
using System.IO;
using System.Collections.Generic;
using OptiSolver.NET.Controller;
using OptiSolver.NET.Services.Base;
using OptiSolver.NET.Core;

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
                Console.WriteLine();

                Console.Write("Enter path to input file: ");
                var path = (Console.ReadLine() ?? "").Trim();
                if (string.IsNullOrWhiteSpace(path))
                {
                    Console.WriteLine("No path given. Exiting.");
                    return;
                }
                if (!System.IO.File.Exists(path))
                {
                    Console.WriteLine($"File not found: {path}");
                    Console.WriteLine();
                    continue; // ask again
                }

                // 2) Solver selection
                Console.WriteLine();
                Console.WriteLine("Choose solver:");
                Console.WriteLine("  1) Primal Simplex (Tableau)");
                Console.WriteLine("  2) Revised Simplex (Two-Phase)   [default]");
                Console.WriteLine("  3) Branch & Bound 0-1 Knapsack   (single ≤ constraint + binary)");
                Console.Write("Selection [1-3]: ");
                var choice = (Console.ReadLine() ?? "").Trim();

                string solverKey = choice switch
                {
                    "1" => "tableau",
                    "3" => "knapsack",
                    _ => "revised",
                };

                // 3) Basic options
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
                try
                {
                    result = controller.SolveFromFile(path, solverKey, options);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unexpected error: {ex.Message}");
                    Console.WriteLine();
                    goto AskAgain;
                }

                IO.OutputWriter.WriteToConsole(result);

                string defaultResults = System.IO.Path.ChangeExtension(path, ".results.txt");
                string defaultLog = System.IO.Path.ChangeExtension(path, ".log.txt");

                IO.OutputWriter.WriteFullResultToFile(result, defaultResults);
                Console.WriteLine($"Full results auto-saved: {defaultResults}");

                Console.Write($"Write FULL results (canonical + all iterations) to file? (y/N) [{defaultResults}]: ");
                var writeFull = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
                if (writeFull == "y" || writeFull == "yes")
                {
                    Console.Write("Path for results file (Enter for default): ");
                    var outPath = (Console.ReadLine() ?? "").Trim();
                    outPath = string.IsNullOrWhiteSpace(outPath) ? defaultResults : outPath;
                    IO.OutputWriter.WriteFullResultToFile(result, outPath);
                    Console.WriteLine($"Full results written: {outPath}");
                }

                Console.Write($"Write iteration log to file? (y/N) [{defaultLog}]: ");
                var save = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
                if (save == "y" || save == "yes")
                {
                    Console.Write("Path for log file (Enter for default): ");
                    var logPath = (Console.ReadLine() ?? "").Trim();
                    logPath = string.IsNullOrWhiteSpace(logPath) ? defaultLog : logPath;
                    IO.OutputWriter.WriteLogToFile(result, logPath);
                    Console.WriteLine($"Log written: {logPath}");
                }

                // (Optional) Sensitivity submenu when optimal
                if (result.IsOptimal || result.Status == SolutionStatus.AlternativeOptimal)
                {
                    Console.WriteLine();
                    Console.WriteLine("Run sensitivity analysis? (y/N): ");
                    var runSa = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
                    if (runSa == "y" || runSa == "yes")
                    {
                        // TODO: call into your SensitivityAnalyser / RangeAnalyser / ShadowPriceCalculator / DualityAnalyser
                        // Show a submenu here and write outputs (also 3 d.p.) to console and/or a separate file.
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
    }
}