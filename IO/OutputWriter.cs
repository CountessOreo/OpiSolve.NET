using System;
using System.IO;
using System.Linq;
using System.Text;
using OptiSolver.NET.Services.Base;
using OptiSolver.NET.Core;
using OptiSolver.NET.UI;

namespace OptiSolver.NET.IO
{
    internal static class OutputWriter
    {
        // ----------------- helpers -----------------
        private static double DisplayObj(SolutionResult r)
        {
            // Print in user-sense: flip if it's a Max problem and the solver stored min-form.
            // If r.ObjectiveValue has already been normalized upstream, this still prints correctly.
            if (double.IsNaN(r.ObjectiveValue))
                return double.NaN;
            return r.ObjectiveSense == ObjectiveType.Maximize ? -r.ObjectiveValue : r.ObjectiveValue;
        }

        private static string Vec(double[] x) =>
            x == null || x.Length == 0
                ? "[]"
                : $"[ {string.Join(", ", x.Select(DisplayHelper.Round3))} ]";

        private static (string Key, string Log) TryGetLog(SolutionResult r)
        {
            if (r?.Info == null)
                return (null, null);

            // Prefer the full canonical+iterations block if provided
            if (r.Info.TryGetValue("CanonicalAndIterations", out var full) && full is string sFull)
                return ("CanonicalAndIterations", sFull);

            if (r.Info.TryGetValue("IterationLog", out var iterObj) && iterObj is string s1)
                return ("IterationLog", s1);

            if (r.Info.TryGetValue("Log", out var s2Obj) && s2Obj is string s2)
                return ("Log", s2);

            return (null, null);
        }

        private static string ResolveStyle(SolutionResult r)
        {
            // If the solver stamped a style, use it.
            if (r?.Info != null && r.Info.TryGetValue("Style", out var style) && style is string ss && !string.IsNullOrWhiteSpace(ss))
                return ss;

            // Fallback heuristics (legacy)
            if (r?.AlgorithmUsed?.Contains("Primal Simplex") == true)
                return "Tableau";
            if (r?.AlgorithmUsed?.Contains("Revised Simplex") == true)
                return "Revised (product form & price-out)";
            if (r?.AlgorithmUsed?.StartsWith("Branch & Bound") == true)
                return "Branch & Bound";
            return "N/A";
        }

        private static string RelaxationEngine(SolutionResult r)
        {
            if (r?.Info != null && r.Info.TryGetValue("RelaxationEngine", out var eng) && eng is string se && !string.IsNullOrWhiteSpace(se))
            {
                return se switch
                {
                    "tableau" => "Primal Simplex (Tableau)",
                    "revised" => "Revised Simplex (Two-Phase)",
                    _ => se
                };
            }
            return null;
        }

        // ----------------- console summary -----------------
        public static void WriteToConsole(SolutionResult r)
        {
            Console.WriteLine();
            Console.WriteLine("=== SOLUTION SUMMARY ===");
            Console.WriteLine($"Algorithm : {r.AlgorithmUsed}");
            Console.WriteLine($"Status    : {r.Status}");
            if (!string.IsNullOrWhiteSpace(r.Message))
                Console.WriteLine($"Message   : {r.Message}");

            var style = ResolveStyle(r);
            Console.WriteLine($"Style     : {style}");

            var relax = RelaxationEngine(r);
            if (!string.IsNullOrWhiteSpace(relax))
                Console.WriteLine($"Relaxation: {relax}");

            if (r.IsOptimal || r.Status == SolutionStatus.AlternativeOptimal)
            {
                var disp = DisplayObj(r);
                Console.WriteLine($"Objective : {DisplayHelper.Round3(disp)}");

                if (r.VariableValues != null && r.VariableValues.Length > 0)
                    Console.WriteLine($"x*        : {Vec(r.VariableValues)}");

                if (r.ReducedCosts != null)
                    Console.WriteLine($"rc (red.) : [ {string.Join(", ", r.ReducedCosts.Select(DisplayHelper.Round3))} ]");

                if (r.DualValues != null)
                    Console.WriteLine($"dual y    : [ {string.Join(", ", r.DualValues.Select(DisplayHelper.Round3))} ]");

                if (r.ShadowPrices != null)
                    Console.WriteLine($"shadow π  : [ {string.Join(", ", r.ShadowPrices.Select(DisplayHelper.Round3))} ]");

                if (r.HasAlternateOptima)
                    Console.WriteLine("Note      : Alternate optimal solutions exist.");
            }

            Console.WriteLine($"Iterations: {r.Iterations}");
            Console.WriteLine($"SolveTime : {r.SolveTimeMs:0.00} ms");

            // Show short log preview if available
            var (key, log) = TryGetLog(r);
            if (!string.IsNullOrEmpty(log))
            {
                Console.WriteLine();
                Console.WriteLine("=== ITERATION LOG (preview) ===");
                var lines = (log ?? "").Split('\n');
                Console.WriteLine(string.Join(Environment.NewLine, lines.Take(50)));
                if (lines.Length > 50)
                    Console.WriteLine("... (truncated)");
            }
        }

        // ----------------- full result to file -----------------
        public static void WriteFullResultToFile(SolutionResult r, string filePath)
        {
            var sb = new StringBuilder();

            // Header
            sb.AppendLine("=== OPTISOLVER.NET RESULT ===");
            sb.AppendLine($"Algorithm : {r.AlgorithmUsed}");
            sb.AppendLine($"Status    : {r.Status}");
            if (!string.IsNullOrWhiteSpace(r.Message))
                sb.AppendLine($"Message   : {r.Message}");

            var style = ResolveStyle(r);
            sb.AppendLine($"Style     : {style}");

            var relax = RelaxationEngine(r);
            if (!string.IsNullOrWhiteSpace(relax))
                sb.AppendLine($"Relaxation: {relax}");

            var disp = DisplayObj(r);
            sb.AppendLine($"Objective : {(double.IsNaN(disp) ? "NaN" : UI.DisplayHelper.Round3(disp))}");
            sb.AppendLine($"Iterations: {r.Iterations}");
            sb.AppendLine($"SolveTime : {r.SolveTimeMs:0.000} ms");
            sb.AppendLine();

            // Primal solution
            if (r.VariableValues != null && r.VariableValues.Length > 0)
                sb.AppendLine($"x*        : {Vec(r.VariableValues)}");

            // Reduced costs / duals / shadow prices
            if (r.ReducedCosts != null)
                sb.AppendLine($"reduced    : [ {string.Join(", ", r.ReducedCosts.Select(UI.DisplayHelper.Round3))} ]");
            if (r.DualValues != null)
                sb.AppendLine($"dual y     : [ {string.Join(", ", r.DualValues.Select(UI.DisplayHelper.Round3))} ]");
            if (r.ShadowPrices != null)
                sb.AppendLine($"shadow π   : [ {string.Join(", ", r.ShadowPrices.Select(UI.DisplayHelper.Round3))} ]");
            if (r.HasAlternateOptima)
                sb.AppendLine("Note      : Alternate optimal solutions exist.");
            sb.AppendLine();

            // Canonical form + ALL iterations (from solver log)
            var log = TryGetLog(r).Log;
            if (!string.IsNullOrEmpty(log))
            {
                sb.AppendLine("=== CANONICAL FORM & ITERATIONS ===");
                // We keep the solver's raw block intact. If you want these internal
                // values automatically “user-sense” too, move the normalization into
                // the solver when composing this block (recommended).
                sb.AppendLine(log);
            }
            else
            {
                sb.AppendLine("(No iteration log available from solver.)");
            }

            File.WriteAllText(filePath, sb.ToString());
        }

        // ----------------- iteration log only -----------------
        public static void WriteLogToFile(SolutionResult r, string filePath)
        {
            var (_, log) = TryGetLog(r);
            File.WriteAllText(filePath, string.IsNullOrEmpty(log) ? "No iteration log available." : log);
        }
    }
}
