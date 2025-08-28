using System;
using System.Linq;
using System.Text;
using OptiSolver.NET.Core;
using OptiSolver.NET.Services.Base;
using OptiSolver.NET.Services.Simplex;

namespace OptiSolver.NET.Analysis
{
    public static class DualityAnalyser
    {
        /// <summary>
        /// Check duality on the continuous relaxation.
        /// Always reports user-sense objectives (no sign confusion).
        /// </summary>
        public static string CheckAndSummarize(LPModel originalModel, SolutionResult lastResult = null)
        {
            // 1) Continuous relaxation (ints/binaries -> continuous x >= 0, keep binary UB<=1)
            var p = MakeContinuousRelaxation(originalModel);

            // 2) Solve primal LP with Revised Simplex (to get duals if available)
            var solver = new RevisedSimplexSolver();
            var pr = solver.Solve(p);
            pr.ObjectiveSense = p.ObjectiveType; // annotate for downstream formatters

            var sb = new StringBuilder();
            sb.AppendLine("=== Duality Check (Continuous Relaxation) ===");
            sb.AppendLine($"Primal status: {pr.Status}");

            if (!pr.IsOptimal || pr.VariableValues == null)
            {
                sb.AppendLine("Primal not optimal or no solution vector; cannot verify strong duality.");
                return sb.ToString();
            }

            // 3) Compute user-sense primal objective directly from data: z_P = c·x
            double zP_user = DotObjective(p, pr.VariableValues);
            sb.AppendLine($"Primal z* (user-sense): {zP_user:0.000}");

            // 4) If primal produced duals, use b·π; else try explicit dual solve (for standard Max-LEQ)
            var pi = pr.DualValues;
            if (pi == null || pi.Length == 0)
            {
                // Prefer solver-provided y from artifacts (Info["DualY"] or cB^T B^{-1})
                var fromInfo = ShadowPriceCalculator.FromResult(pr);
                if (fromInfo != null)
                    pi = fromInfo;
            }

            if (pi != null && pi.Length == p.Constraints.Count)
            {
                sb.AppendLine($"Dual variables (π): [ {string.Join(", ", pi.Select(v => v.ToString("0.000")))} ]");

                double zD_user = DotBpi(p, pi);
                double gap = Math.Abs(zP_user - zD_user);
                bool strong = gap <= 1e-6;

                sb.AppendLine($"Dual z* (b·π): {zD_user:0.000}");
                sb.AppendLine($"Strong Duality: {(strong ? "YES" : "NO")} (|z_P - z_D|={gap:0.000000})");
                return sb.ToString();
            }

            // 5) No duals from primal run; attempt explicit dual if in standard Max-LEQ form
            if (IsStandardMaxLEQ(p))
            {
                var d = BuildDualForStandardMaxLEQ(p); // Min b^T y  s.t. A^T y ≥ c, y ≥ 0
                var dr = solver.Solve(d);
                dr.ObjectiveSense = d.ObjectiveType;

                if (dr.IsOptimal && dr.VariableValues != null)
                {
                    // Compute dual objective from y (user-sense) as b·y
                    double zD_user = DotBWithDualVars(d, dr.VariableValues); // careful: dual's variables are the y's
                    double gap = Math.Abs(zP_user - zD_user);
                    bool strong = gap <= 1e-6;

                    sb.AppendLine("Dual variables not returned by primal; solved explicit dual instead.");
                    sb.AppendLine($"Dual status: {dr.Status}");
                    sb.AppendLine($"Dual z* (user-sense, b·y): {zD_user:0.000}");
                    sb.AppendLine($"Strong Duality: {(strong ? "YES" : "NO")} (|z_P - z_D|={gap:0.000000})");
                }
                else
                {
                    sb.AppendLine("Dual variables not available. Explicit dual solve did not reach optimality.");
                }
            }
            else
            {
                sb.AppendLine("Dual variables not available. (Primal not in standard Max-≤-x≥0 form for explicit dual build.)");
                sb.AppendLine("Tip: solve the relaxation with Revised Simplex to produce dual artifacts.");
            }

            return sb.ToString();
        }

        // ----------------- helpers (unchanged) -----------------
        private static double DotObjective(LPModel p, double[] x)
        {
            int n = Math.Min(p.Variables.Count, x?.Length ?? 0);
            double z = 0.0;
            for (int j = 0; j < n; j++)
                z += p.Variables[j].Coefficient * x[j];
            return z;
        }

        private static double DotBpi(LPModel p, double[] pi)
        {
            int m = Math.Min(p.Constraints.Count, pi?.Length ?? 0);
            double z = 0.0;
            for (int i = 0; i < m; i++)
                z += p.Constraints[i].RightHandSide * pi[i];
            return z;
        }

        private static double DotBWithDualVars(LPModel dual, double[] y)
        {
            int m = Math.Min(dual.Variables.Count, y?.Length ?? 0);
            double z = 0.0;
            for (int i = 0; i < m; i++)
                z += dual.Variables[i].Coefficient * y[i];
            return z;
        }

        private static bool IsStandardMaxLEQ(LPModel p)
        {
            if (p.ObjectiveType != ObjectiveType.Maximize)
                return false;
            if (p.Variables.Any(v => v.LowerBound < -1e-12))
                return false;
            if (p.Constraints.Any(c => c.Relation != ConstraintRelation.LessThanOrEqual))
                return false;
            return true;
        }

        private static LPModel MakeContinuousRelaxation(LPModel m)
        {
            var r = new LPModel
            {
                ObjectiveType = m.ObjectiveType,
                Name = (m.Name ?? "Model") + " — Continuous Relaxation"
            };
            foreach (var v in m.Variables)
            {
                r.Variables.Add(new Variable(v.Index, v.Name, v.Coefficient, VariableType.Positive, Math.Max(0, v.LowerBound), v.UpperBound));
            }
            foreach (var c in m.Constraints)
            {
                var nc = new Constraint { Relation = c.Relation, RightHandSide = c.RightHandSide };
                foreach (var aij in c.Coefficients)
                    nc.Coefficients.Add(aij);
                r.Constraints.Add(nc);
            }
            return r;
        }

        /// <summary>Build the dual for Max c^T x, s.t. A x ≤ b, x ≥ 0  →  Min b^T y, s.t. A^T y ≥ c, y ≥ 0</summary>
        private static LPModel BuildDualForStandardMaxLEQ(LPModel p)
        {
            int m = p.Constraints.Count;
            int n = p.Variables.Count;

            var d = new LPModel
            {
                ObjectiveType = ObjectiveType.Minimize,
                Name = (p.Name ?? "Primal") + " — Dual"
            };

            // Dual variables y_i ≥ 0 with objective coeff b_i
            for (int i = 0; i < m; i++)
            {
                double bi = p.Constraints[i].RightHandSide;
                d.Variables.Add(new Variable(i, $"y{i + 1}", bi, VariableType.Positive, 0, double.PositiveInfinity));
            }

            // Dual constraints: A^T y ≥ c
            for (int j = 0; j < n; j++)
            {
                var cons = new Constraint
                {
                    Relation = ConstraintRelation.GreaterThanOrEqual,
                    RightHandSide = p.Variables[j].Coefficient
                };
                for (int i = 0; i < m; i++)
                    cons.Coefficients.Add(p.Constraints[i].Coefficients[j]); // Aᵀ
                d.Constraints.Add(cons);
            }

            return d;
        }
    }
}