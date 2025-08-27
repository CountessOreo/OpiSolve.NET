using System;
using System.Collections.Generic;
using OptiSolver.NET.Core;
using OptiSolver.NET.IO;
using OptiSolver.NET.Services.Base;
using OptiSolver.NET.Services.Simplex;
using OptiSolver.NET.Services.BranchAndBound;
using OptiSolver.NET.Exceptions;
using OptiSolver.NET.Services.Nonlinear;

namespace OptiSolver.NET.Controller
{
    /// <summary>
    /// Orchestrates: parse input -> choose solver -> solve -> return SolutionResult.
    /// </summary>
    public sealed class SolverController
    {
        public LPModel LastModel { get; private set; }

        /// <summary>
        /// Parse an input file and solve it with the selected solver.
        /// </summary>
        /// <param name="filePath">Path to input text file (see InputParser format)</param>
        /// <param name="solverKey">
        /// "tableau" => Primal Simplex (Tableau),
        /// "revised" => Revised Simplex (Two-Phase),
        /// "knapsack" => Branch & Bound 0-1 Knapsack (only for ≤ single-constraint binary),
        /// "bb-ilp"  => Branch & Bound (MILP) with Revised relaxations (default),
        /// "bb-ilp-tableau" => Branch & Bound (MILP) with Primal Simplex (Tableau) relaxations.
        /// </param>
        /// <param name="options">Optional solver options</param>
        public SolutionResult SolveFromFile(string filePath, string solverKey = "revised", Dictionary<string, object> options = null)
        {
            try
            {
                var parser = new InputParser();
                LastModel = parser.ParseFile(filePath);

                // Normalize options for B&B tableau relaxations if requested by key
                options = EnsureBBRelaxationEngineIfRequested(solverKey, options);

                var solver = CreateSolver(solverKey, LastModel);
                if (solver == null)
                    return SolutionResult.CreateError("Controller", $"Unknown or unsupported solver key: '{solverKey}'");

                return solver.Solve(LastModel, options);
            }
            catch (InvalidInputException ex)
            {
                return SolutionResult.CreateError("Input", ex.Message);
            }
            catch (InfeasibleSolutionException ex)
            {
                return SolutionResult.CreateInfeasible("N/A", ex.Message);
            }
            catch (UnboundedSolutionException ex)
            {
                return SolutionResult.CreateUnbounded("N/A", ex.Message);
            }
            catch (AlgorithmException ex)
            {
                return SolutionResult.CreateError("Algorithm", ex.Message);
            }
            catch (Exception ex)
            {
                return SolutionResult.CreateError("Controller", ex.Message);
            }
        }

        // Solve an already-parsed model
        public SolutionResult SolveModel(LPModel model, string solverKey = "revised", Dictionary<string, object> options = null)
        {
            try
            {
                if (model == null)
                    return SolutionResult.CreateError("Controller", "Model cannot be null");

                LastModel = model;

                // Normalize options for B&B tableau relaxations if requested by key
                options = EnsureBBRelaxationEngineIfRequested(solverKey, options);

                var solver = CreateSolver(solverKey, model);
                if (solver == null)
                    return SolutionResult.CreateError("Controller", $"Unknown or unsupported solver key: '{solverKey}'");

                return solver.Solve(model, options);
            }
            catch (InvalidInputException ex)
            {
                return SolutionResult.CreateError("Input", ex.Message);
            }
            catch (InfeasibleSolutionException ex)
            {
                return SolutionResult.CreateInfeasible("N/A", ex.Message);
            }
            catch (UnboundedSolutionException ex)
            {
                return SolutionResult.CreateUnbounded("N/A", ex.Message);
            }
            catch (AlgorithmException ex)
            {
                return SolutionResult.CreateError("Algorithm", ex.Message);
            }
            catch (Exception ex)
            {
                return SolutionResult.CreateError("Controller", ex.Message);
            }
        }

        /// <summary>
        /// Choose an ISolver implementation for the model and key.
        /// </summary>
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
                {
                    var bb = new BranchBoundKnapsackSolver();
                    return bb.CanSolve(model) ? bb : null;
                }

                case "bb":
                case "ilp":
                case "bb-ilp":
                case "branchbound":
                case "branch-and-bound":
                case "bb-tableau":                 
                case "bb-ilp-tableau":            
                case "branch-and-bound-tableau":  
                {
                    var bb = new BranchBoundILPSolver();
                    return bb.CanSolve(model) ? bb : null;   
                }

                case "nonlinear":
                case "nonlinear-demo":
                case "nl":
                return new NonlinearSolver();
            }

            // Fallback: auto-detect knapsack
            var bbTry = new BranchBoundKnapsackSolver();
            if (bbTry.CanSolve(model))
                return bbTry;

            // Default to Revised Simplex for general LP
            return new RevisedSimplexSolver();
        }

        /// <summary>
        /// If the selected solver key implies B&B(ILP) with tableau relaxations,
        /// ensure options["BBRelaxationEngine"]="tableau". Otherwise leave options as-is.
        /// </summary>
        private static Dictionary<string, object> EnsureBBRelaxationEngineIfRequested(string solverKey, Dictionary<string, object> options)
        {
            var key = (solverKey ?? "").Trim().ToLowerInvariant();
            bool wantsBBTableau =
                key == "bb-tableau" ||
                key == "bb-ilp-tableau" ||
                key == "branch-and-bound-tableau";

            if (!wantsBBTableau)
                return options;

            options ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            options["BBRelaxationEngine"] = "tableau";
            return options;
        }
    }
}
