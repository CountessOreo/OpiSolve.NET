using System;
using System.Collections.Generic;
using OptiSolver.NET.Core;
using OptiSolver.NET.IO;
using OptiSolver.NET.Services.Base;
using OptiSolver.NET.Services.Simplex;
using OptiSolver.NET.Services.BranchAndBound;
using OptiSolver.NET.Exceptions;

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
        /// "knapsack" => Branch & Bound 0-1 Knapsack (only for ≤ single-constraint binary)
        /// </param>
        /// <param name="options">Optional solver options</param>
        public SolutionResult SolveFromFile(string filePath, string solverKey = "revised", Dictionary<string, object> options = null)
        {
            try
            {
                var parser = new InputParser();
                LastModel = parser.ParseFile(filePath);

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

        /// <summary>
        /// Choose an ISolver implementation for the model and key.
        /// </summary>
        private ISolver CreateSolver(string key, LPModel model)
        {
            key = (key ?? "").Trim().ToLowerInvariant();

            // Prefer explicit choice
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
            }

            // Fallback: auto-detect
            var bbTry = new BranchBoundKnapsackSolver();
            if (bbTry.CanSolve(model))
                return bbTry;

            // Default general LP solver
            return new RevisedSimplexSolver();
        }
    }
}