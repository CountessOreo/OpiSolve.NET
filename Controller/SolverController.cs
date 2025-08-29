using OptiSolver.NET.Core;
using OptiSolver.NET.Exceptions;
using OptiSolver.NET.IO;
using OptiSolver.NET.Services.Base;
using OptiSolver.NET.Services.BranchAndBound;
using OptiSolver.NET.Services.CuttingPlane;
using OptiSolver.NET.Services.Nonlinear;
using OptiSolver.NET.Services.Simplex;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OptiSolver.NET.Controller
{
    public sealed class SolverController
    {
        public LPModel LastModel { get; private set; }

        public SolutionResult SolveFromFile(string filePath, string solverKey = "revised", Dictionary<string, object> options = null)
        {
            try
            {
                var parser = new InputParser();
                LastModel = parser.ParseFile(filePath);

                options = EnsureBBRelaxationEngineIfRequested(solverKey, options);

                var solver = CreateSolver(solverKey, LastModel);
                if (solver == null)
                    return SolutionResult.CreateError("Controller", $"Unknown or unsupported solver key: '{solverKey}'");

                if (IsNonlinearKey(solverKey) && solver is NonlinearSolver)
                {
                    var auto = BuildNonlinear1DOptions(LastModel);
                    options = MergeOptionsPreferCaller(auto, options);
                    if (!options.ContainsKey("Function"))
                        return SolutionResult.CreateError(solver.AlgorithmName, "Function expression missing. Expected OBJECTIVE: minimize f(x) = <expr>.");
                }

                return solver.Solve(LastModel, options);
            }
            catch (InvalidInputException ex) { return SolutionResult.CreateError("Input", ex.Message); }
            catch (InfeasibleSolutionException ex) { return SolutionResult.CreateInfeasible("N/A", ex.Message); }
            catch (UnboundedSolutionException ex) { return SolutionResult.CreateUnbounded("N/A", ex.Message); }
            catch (AlgorithmException ex) { return SolutionResult.CreateError("Algorithm", ex.Message); }
            catch (Exception ex) { return SolutionResult.CreateError("Controller", ex.Message); }
        }

        public SolutionResult SolveModel(LPModel model, string solverKey = "revised", Dictionary<string, object> options = null)
        {
            try
            {
                if (model == null)
                    return SolutionResult.CreateError("Controller", "Model cannot be null");

                LastModel = model;
                options = EnsureBBRelaxationEngineIfRequested(solverKey, options);

                var solver = CreateSolver(solverKey, model);
                if (solver == null)
                    return SolutionResult.CreateError("Controller", $"Unknown or unsupported solver key: '{solverKey}'");

                if (IsNonlinearKey(solverKey) && solver is NonlinearSolver)
                {
                    var auto = BuildNonlinear1DOptions(model);
                    options = MergeOptionsPreferCaller(auto, options);
                    if (!options.ContainsKey("Function"))
                        return SolutionResult.CreateError(solver.AlgorithmName, "Function expression missing. Expected OBJECTIVE: minimize f(x) = <expr>.");
                }

                return solver.Solve(model, options);
            }
            catch (InvalidInputException ex) { return SolutionResult.CreateError("Input", ex.Message); }
            catch (InfeasibleSolutionException ex) { return SolutionResult.CreateInfeasible("N/A", ex.Message); }
            catch (UnboundedSolutionException ex) { return SolutionResult.CreateUnbounded("N/A", ex.Message); }
            catch (AlgorithmException ex) { return SolutionResult.CreateError("Algorithm", ex.Message); }
            catch (Exception ex) { return SolutionResult.CreateError("Controller", ex.Message); }
        }

        private ISolver CreateSolver(string key, LPModel model)
        {
            key = (key ?? "").Trim().ToLowerInvariant();
            switch (key)
            {
                case "tableau":
                case "primal":
                case "primal-tableau":
                return new PrimalSimplexTableauSolver();
                case "revised":
                case "revised-simplex":
                case "simplex":
                return new RevisedSimplexSolver();
                case "knapsack":
                case "bb-knapsack":
                case "branchbound-knapsack":
                var bbk = new BranchBoundKnapsackSolver();
                return bbk.CanSolve(model) ? bbk : null;
                case "bb":
                case "ilp":
                case "bb-ilp":
                case "branchbound":
                case "branch-and-bound":
                case "bb-tableau":
                case "bb-ilp-tableau":
                case "branch-and-bound-tableau":
                var bbi = new BranchBoundILPSolver();
                return bbi.CanSolve(model) ? bbi : null;
                case "cutting":
                case "gomory":
                return new CuttingPlaneSolver();
                case "nonlinear":
                case "nonlinear-demo":
                case "nl":
                return new NonlinearSolver();
            }
            var bbTry = new BranchBoundKnapsackSolver();
            if (bbTry.CanSolve(model))
                return bbTry;
            return new RevisedSimplexSolver();
        }

        private static Dictionary<string, object> EnsureBBRelaxationEngineIfRequested(string solverKey, Dictionary<string, object> options)
        {
            var key = (solverKey ?? "").Trim().ToLowerInvariant();
            bool wantsBBTableau = key == "bb-tableau" || key == "bb-ilp-tableau" || key == "branch-and-bound-tableau";
            if (!wantsBBTableau)
                return options;

            options ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            options["BBRelaxationEngine"] = "tableau";
            return options;
        }

        // ---------------- Nonlinear helpers ----------------
        private static bool IsNonlinearKey(string key)
        {
            key = (key ?? "").Trim().ToLowerInvariant();
            return key == "nonlinear" || key == "nonlinear-demo" || key == "nl";
        }

        private static Dictionary<string, object> BuildNonlinear1DOptions(LPModel model)
        {
            var opts = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // Function (from LPModel.NonlinearExpr as we added earlier)
            if (!string.IsNullOrWhiteSpace(model?.NonlinearExpr))
                opts["Function"] = model.NonlinearExpr!.Trim();

            // Bounds & initial value from variable "x"
            var xVar = model?.Variables?.FirstOrDefault(v =>
                v?.Name?.Equals("x", StringComparison.OrdinalIgnoreCase) == true);

            if (xVar != null)
            {
                // LowerBound
                if (!double.IsNaN(xVar.LowerBound) && !double.IsInfinity(xVar.LowerBound))
                    opts["LowerBound"] = xVar.LowerBound;

                // UpperBound
                if (!double.IsNaN(xVar.UpperBound) && !double.IsInfinity(xVar.UpperBound))
                    opts["UpperBound"] = xVar.UpperBound;

                // InitialX from Variable.Value (parser should set this from INITIAL: x0 = ...)
                if (!double.IsNaN(xVar.Value) && !double.IsInfinity(xVar.Value))
                    opts["InitialX"] = xVar.Value;
            }

            // Optional tolerance from LPModel.NonlinearTol
            if (model?.NonlinearTol is double tol && tol > 0 && !double.IsInfinity(tol))
                opts["Tolerance"] = tol;

            return opts;
        }

        private static Dictionary<string, object> MergeOptionsPreferCaller(Dictionary<string, object> autoFromFile, Dictionary<string, object> caller)
        {
            if (autoFromFile == null && caller == null)
                return null;
            if (autoFromFile == null)
                return new Dictionary<string, object>(caller, StringComparer.OrdinalIgnoreCase);
            if (caller == null)
                return new Dictionary<string, object>(autoFromFile, StringComparer.OrdinalIgnoreCase);

            var merged = new Dictionary<string, object>(autoFromFile, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in caller)
                merged[kv.Key] = kv.Value;
            return merged;
        }
    }
}
