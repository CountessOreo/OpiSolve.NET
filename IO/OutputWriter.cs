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
        public static void WriteToConsole(SolutionResult r)
        {
            Console.WriteLine();
            Console.WriteLine("=== SOLUTION SUMMARY ===");
            Console.WriteLine($"Algorithm : {r.AlgorithmUsed}");
            Console.WriteLine($"Status    : {r.Status}");
            if (!string.IsNullOrWhiteSpace(r.Message))
                Console.WriteLine($"Message   : {r.Message}");

            if (r.IsOptimal || r.Status == SolutionStatus.AlternativeOptimal)
            {
                Console.WriteLine($"Objective : {DisplayHelper.Round3(r.ObjectiveValue)}");
                if (r.VariableValues != null && r.VariableValues.Length > 0)
                    Console.WriteLine($"x*        : [ {string.Join(", ", r.VariableValues.Select(DisplayHelper.Round3))} ]");

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
                var preview = string.Join(Environment.NewLine, (log ?? "").Split('\n').Take(50));
                Console.WriteLine(preview);
                if ((log ?? "").Split('\n').Length > 50)
                    Console.WriteLine("... (truncated)");
            }
        }

        public static void WriteLogToFile(SolutionResult r, string filePath)
        {
            var (_, log) = TryGetLog(r);
            if (string.IsNullOrEmpty(log))
            {
                File.WriteAllText(filePath, "No iteration log available.");
                return;
            }
            File.WriteAllText(filePath, log);
        }

        private static (string Key, string Log) TryGetLog(SolutionResult r)
        {
            if (r?.Info == null)
                return (null, null);

            // These are the typical keys used by your solvers
            if (r.Info.TryGetValue("IterationLog", out var iterObj) && iterObj is string s1)
                return ("IterationLog", s1);

            if (r.Info.TryGetValue("Log", out var s2Obj) && s2Obj is string s2)
                return ("Log", s2);

            return (null, null);
        }

        public static void WriteFullResultToFile(SolutionResult r, string filePath)
        {
            var sb = new StringBuilder();

            // Header
            sb.AppendLine("=== OPTISOLVER.NET RESULT ===");
            sb.AppendLine($"Algorithm : {r.AlgorithmUsed}");
            sb.AppendLine($"Status    : {r.Status}");
            if (!string.IsNullOrWhiteSpace(r.Message))
                sb.AppendLine($"Message   : {r.Message}");
            sb.AppendLine($"Objective : {(double.IsNaN(r.ObjectiveValue) ? "NaN" : UI.DisplayHelper.Round3(r.ObjectiveValue))}");
            sb.AppendLine($"Iterations: {r.Iterations}");
            sb.AppendLine($"SolveTime : {r.SolveTimeMs:0.000} ms");
            sb.AppendLine();

            // Primal solution
            if (r.VariableValues != null && r.VariableValues.Length > 0)
                sb.AppendLine($"x*        : [ {string.Join(", ", r.VariableValues.Select(UI.DisplayHelper.Round3))} ]");

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
                sb.AppendLine(log); // your simplex log already prints tableau rows @ 3 d.p.
            }
            else
            {
                sb.AppendLine("(No iteration log available from solver.)");
            }

            File.WriteAllText(filePath, sb.ToString());
        }

    }
}