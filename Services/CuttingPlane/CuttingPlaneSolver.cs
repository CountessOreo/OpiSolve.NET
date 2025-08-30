using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OptiSolver.NET.Core;
using OptiSolver.NET.Services.Base;
using OptiSolver.NET.Services.Simplex;

namespace OptiSolver.NET.Services.CuttingPlane
{
    /// <summary>
    /// Gomory Fractional Cutting-Plane solver built on top of the Revised Simplex engine.
    ///
    /// Notes/Assumptions:
    /// - Designed for pure integer/binary models with x >= 0 (i.e., no unrestricted or negative-substitution vars).
    /// - Iteratively solves the LP relaxation with Revised Simplex and adds Gomory cuts
    ///   derived from fractional basic rows until an integer solution is found or limits are hit.
    /// - Iteration log aggregates the canonical form header and all Revised Simplex iteration logs for audit.
    ///
    /// Exposes a few options via the standard options dictionary:
    ///   MaxCuts        (int, default 50)
    ///   MaxIterations  (int, default 200)
    ///   Tolerance      (double, default 1e-9)
    ///   Verbose        (bool, default false)
    /// </summary>
    public sealed class CuttingPlaneSolver : SolverBase
    {
        public override string AlgorithmName => "Cutting Plane (Gomory Fractional)";
        public override string Description =>
            "Iterative Gomory cutting-plane method over LP relaxations solved with Revised Simplex.";

        #region Options
        public override Dictionary<string, object> GetDefaultOptions() => new()
        {
            { "MaxCuts", 50 },
            { "MaxIterations", 200 },
            { "Tolerance", 1e-9 },
            { "Verbose", false }
        };
        #endregion

        public override bool CanSolve(LPModel model)
        {
            if (model == null || model.Variables == null || model.Variables.Count == 0)
                return false;

            // Pure IP: all variables must be Integer or Binary and have lower bounds >= 0.
            for (int i = 0; i < model.Variables.Count; i++)
            {
                var v = model.Variables[i];
                if (!(v.Type == VariableType.Integer || v.Type == VariableType.Binary))
                    return false;

                // Disallow unrestricted/negative-substitution for this implementation
                if (v.LowerBound < -1e-12)
                    return false;
            }
            return true;
        }

        public override SolutionResult Solve(LPModel model, Dictionary<string, object> options = null)
        {
            var started = DateTime.UtcNow;
            var opt = MergeOptions(options);
            int maxCuts = GetOption(opt, "MaxCuts", 50);
            int maxIter = GetOption(opt, "MaxIterations", 200);
            double tol = GetOption(opt, "Tolerance", 1e-9);

            // Validate
            var valid = ValidateModel(model);
            if (valid != null)
            {
                valid.ObjectiveSense = model?.ObjectiveType ?? ObjectiveType.Minimize;
                return valid;
            }
            if (!CanSolve(model))
            {
                var err = SolutionResult.CreateError(AlgorithmName, "Cutting-plane requires a pure integer/binary model with x >= 0.");
                err.ObjectiveSense = model.ObjectiveType;
                return err;
            }

            // Working model (we will append cuts to this copy)
            var working = CloneModel(model);

            // Iteration log (we'll append the Revised Simplex logs at each round)
            var log = new StringBuilder();
            log.AppendLine("=== CUTTING PLANE (GOMORY FRACTIONAL) ===");
            log.AppendLine($"Objective sense : {working.ObjectiveType}");
            log.AppendLine($"Variables       : {working.Variables.Count}");
            log.AppendLine($"Constraints     : {working.Constraints.Count}");
            log.AppendLine();

            var revised = new RevisedSimplexSolver();

            int cuts = 0;
            int rounds = 0;
            SolutionResult lastLp = null;

            try
            {
                while (rounds < maxIter && cuts < maxCuts)
                {
                    rounds++;

                    // 1) Solve LP relaxation
                    lastLp = revised.Solve(working);

                    // Append the inner engine's iteration log
                    if (lastLp?.Info != null && lastLp.Info.TryGetValue("IterationLog", out var iterObj) && iterObj is string innerLog)
                    {
                        log.AppendLine();
                        log.AppendLine($"--- LP RELAXATION ROUND #{rounds} ---");
                        log.AppendLine(innerLog);
                    }

                    if (lastLp == null)
                    {
                        var err = SolutionResult.CreateError(AlgorithmName, "LP relaxation returned null result.");
                        err.ObjectiveSense = model.ObjectiveType;
                        err.Info["IterationLog"] = log.ToString();
                        return err;
                    }

                    // Handle infeasible/unbounded before cut generation
                    if (lastLp.IsInfeasible)
                    {
                        var infeas = SolutionResult.CreateInfeasible(AlgorithmName, "LP relaxation infeasible (cannot proceed with cuts).");
                        infeas.ObjectiveSense = model.ObjectiveType;
                        infeas.Info["IterationLog"] = log.ToString();
                        return infeas;
                    }
                    if (lastLp.IsUnbounded)
                    {
                        var unb = SolutionResult.CreateUnbounded(AlgorithmName, "LP relaxation unbounded (cannot proceed with cuts).");
                        unb.ObjectiveSense = model.ObjectiveType;
                        unb.Info["IterationLog"] = log.ToString();
                        return unb;
                    }

                    // 2) Check integrality of current solution (original variable space)
                    var x = lastLp.VariableValues ?? Array.Empty<double>();
                    bool allInt = true;
                    int fracIndex = -1;
                    double fracPart = 0.0;
                    for (int i = 0; i < x.Length; i++)
                    {
                        double f = Fraction(x[i], tol);
                        if (f > tol && f < 1 - tol)
                        {
                            allInt = false;
                            // remember the most fractional one for logging
                            if (f > fracPart)
                            {
                                fracPart = f;
                                fracIndex = i;
                            }
                        }
                    }

                    if (allInt)
                    {
                        double userSense = lastLp.ObjectiveValue;

                        var done = SolutionResult.CreateOptimal(
                            objectiveValue: userSense,
                            variableValues: x,
                            iterations: rounds,
                            algorithm: AlgorithmName,
                            solveTimeMs: (DateTime.UtcNow - started).TotalMilliseconds,
                            message: "Integer optimum found via cutting planes.");

                        // Carry over last artifacts and composed log
                        if (lastLp.Info != null)
                            foreach (var kv in lastLp.Info)
                                done.Info[kv.Key] = kv.Value;

                        done.Info["Style"] = "Gomory fractional cuts over LP relaxations";
                        done.Info["CutsAdded"] = cuts;
                        done.Info["IterationLog"] = log.ToString();

                        // *** Key line for correct display in user-sense (e.g., +2 instead of -2) ***
                        done.ObjectiveSense = model.ObjectiveType;
                        done.Info["ObjectiveSense"] = model.ObjectiveType;

                        return done;
                    }

                    // 3) Build a Gomory fractional cut from a fractional BASIC row
                    var canon = new ModelCanonicalTransformer().Canonicalize(working);
                    if (lastLp.Info == null ||
                        !lastLp.Info.TryGetValue("BasisIndices", out var bObj) || bObj is not int[] basisIdx ||
                        !lastLp.Info.TryGetValue("BInv", out var bInvObj) || bInvObj is not double[,] BInv)
                    {
                        // Revised should export these; if not available, we cannot form cuts safely.
                        var err = SolutionResult.CreateError(AlgorithmName, "Missing basis/BInv artifacts from LP relaxation (required for Gomory cut).");
                        err.ObjectiveSense = model.ObjectiveType;
                        err.Info["IterationLog"] = log.ToString();
                        return err;
                    }

                    int m = canon.ConstraintCount;
                    int nCanon = canon.TotalVariables;

                    // Compute current basic solution x_B = BInv * b
                    var xB = Multiply(BInv, canon.RightHandSide);

                    // Pick a row where (a) the basic variable corresponds to an ORIGINAL integer var and (b) RHS is fractional
                    int chosenRow = -1;
                    int chosenCanonBasic = -1;
                    double chosenRhsFrac = 0.0;

                    for (int i = 0; i < m; i++)
                    {
                        int jB = basisIdx[i];
                        // Skip if this basis column is auxiliary (slack/surplus/artificial)
                        if (!canon.VariableMapping.CanonicalToOriginal.TryGetValue(jB, out var origInfo))
                            continue; // auxiliary

                        // Ensure original var is integer/binary
                        int origIdx = origInfo.OriginalIndex;
                        var vtype = working.Variables[origIdx].Type;
                        if (!(vtype == VariableType.Integer || vtype == VariableType.Binary))
                            continue;

                        double rhs = xB[i];
                        double f = Fraction(rhs, tol);
                        if (f > tol && f < 1 - tol)
                        {
                            chosenRow = i;
                            chosenCanonBasic = jB;
                            chosenRhsFrac = f;
                            break; // first-fractional policy
                        }
                    }

                    // If none found (rare for pure IP), stop defensively
                    if (chosenRow < 0)
                    {
                        log.AppendLine("[STOP] No eligible fractional basic row found for Gomory cut.\n");
                        break;
                    }

                    // Row multiplier u^T = e_i^T * BInv
                    var u = GetRow(BInv, chosenRow);

                    // Row over canonical columns: r_canon = u^T * A
                    var rCanon = Multiply(u, canon.ConstraintMatrix); // length = nCanon

                    // Compress canonical row back to ORIGINAL variable space (handle sign maps safely)
                    int nOrig = working.Variables.Count;
                    var rowOrig = new double[nOrig];
                    for (int k = 0; k < nOrig; k++)
                    {
                        if (!canon.VariableMapping.OriginalToCanonicalSigned.TryGetValue(k, out var list))
                            continue;
                        double sum = 0.0;
                        foreach (var (idx, sign) in list)
                        {
                            // Only structural canonical variables appear in this map
                            sum += sign * rCanon[idx];
                        }
                        rowOrig[k] = sum;
                    }

                    // Gomory fractional cut in original variable space:
                    //   sum_j frac(rowOrig[j]) * x_j >= frac(u^T b)
                    var cutCoeffs = new double[nOrig];
                    for (int j = 0; j < nOrig; j++)
                        cutCoeffs[j] = Fraction(rowOrig[j], tol);
                    double rhsCut = chosenRhsFrac; // == Fraction(u^T b)

                    // Add as a >= constraint
                    AddCutConstraint(working, cutCoeffs, rhsCut, $"gomory_{cuts + 1}");
                    cuts++;

                    // Log the cut
                    log.AppendLine();
                    log.AppendLine($"[Cut #{cuts}] Derived from basic row {chosenRow}, rhs fractional part = {Round3(chosenRhsFrac)}");
                    log.AppendLine($"         Inequality:  sum frac(a_j) x_j >= {Round3(rhsCut)}");
                    log.AppendLine($"         Coeffs(frac): [ {string.Join(", ", cutCoeffs.Select(Round3))} ]");
                    log.AppendLine();
                }

                double rawMinFormLast = double.NaN;
                if (lastLp != null && !double.IsNaN(lastLp.ObjectiveValue))
                    rawMinFormLast = (model.ObjectiveType == ObjectiveType.Maximize)
                        ? -lastLp.ObjectiveValue
                        : lastLp.ObjectiveValue;

                var maxed = SolutionResult.CreateMaxIterationsReached(
                    algorithm: AlgorithmName,
                    iterations: rounds,
                    objectiveValue: rawMinFormLast,
                    variableValues: lastLp?.VariableValues,
                    message: "Reached iteration/cut limit without integer solution."
                );
                maxed.ObjectiveSense = model.ObjectiveType;
                if (lastLp?.Info != null)
                    foreach (var kv in lastLp.Info)
                        maxed.Info[kv.Key] = kv.Value;
                maxed.Info["Style"] = "Gomory fractional cuts over LP relaxations";
                maxed.Info["CutsAdded"] = cuts;
                maxed.Info["IterationLog"] = log.ToString();
                maxed.SolveTimeMs = (DateTime.UtcNow - started).TotalMilliseconds;
                return maxed;
            }
            catch (Exception ex)
            {
                var err = SolutionResult.CreateError(AlgorithmName, ex.Message);
                err.ObjectiveSense = model.ObjectiveType;
                if (lastLp?.Info != null)
                    foreach (var kv in lastLp.Info)
                        err.Info[kv.Key] = kv.Value;
                err.Info["IterationLog"] = log.ToString();
                err.SolveTimeMs = (DateTime.UtcNow - started).TotalMilliseconds;
                return err;
            }
            finally
            {
                // (timing already stamped onto the result objects above)
            }
        }

        #region Helpers
        private static string Round3(double v) => double.IsNaN(v) ? "NaN" : v.ToString("0.000");

        private static double Fraction(double x, double tol)
        {
            // Fractional part in [0, 1), tolerant around integers
            double f = x - Math.Floor(x + tol);
            if (f < tol || f > 1 - tol)
                return 0.0;
            return f;
        }

        private static double[] GetRow(double[,] M, int row)
        {
            int c = M.GetLength(1);
            var v = new double[c];
            for (int j = 0; j < c; j++)
                v[j] = M[row, j];
            return v;
        }

        private static double[] Multiply(double[] uT, double[,] A)
        {
            int m = A.GetLength(0); // rows
            int n = A.GetLength(1); // cols
            if (uT.Length != m)
                throw new InvalidOperationException("Dimension mismatch in u^T * A.");
            var res = new double[n];
            for (int j = 0; j < n; j++)
            {
                double s = 0.0;
                for (int i = 0; i < m; i++)
                    s += uT[i] * A[i, j];
                res[j] = s;
            }
            return res;
        }

        private static double[] Multiply(double[,] M, double[] v)
        {
            int r = M.GetLength(0);
            int c = M.GetLength(1);
            if (v.Length != c)
                throw new InvalidOperationException("Dimension mismatch in M * v.");
            var res = new double[r];
            for (int i = 0; i < r; i++)
            {
                double s = 0.0;
                for (int j = 0; j < c; j++)
                    s += M[i, j] * v[j];
                res[i] = s;
            }
            return res;
        }

        private static void AddCutConstraint(LPModel model, double[] coeffs, double rhs, string name)
        {
            if (coeffs == null || coeffs.Length != model.Variables.Count)
                throw new InvalidOperationException("Cut coefficients must match number of original variables.");

            var c = new Constraint
            {
                Name = name,
                Coefficients = coeffs.ToList(),
                Relation = ConstraintRelation.GreaterThanOrEqual,
                RightHandSide = rhs
            };
            model.Constraints.Add(c);
        }

        private static LPModel CloneModel(LPModel m)
        {
            var copy = new LPModel { ObjectiveType = m.ObjectiveType };

            foreach (var v in m.Variables)
                copy.Variables.Add(new Variable(
                    index: v.Index, name: v.Name, coefficient: v.Coefficient,
                    type: v.Type, lowerBound: v.LowerBound, upperBound: v.UpperBound));

            foreach (var c in m.Constraints)
            {
                var nc = new Constraint
                {
                    Name = c.Name,
                    Relation = c.Relation,
                    RightHandSide = c.RightHandSide
                };
                foreach (var coef in c.Coefficients)
                    nc.Coefficients.Add(coef);
                copy.Constraints.Add(nc);
            }
            return copy;
        }

        #endregion
    }
}
