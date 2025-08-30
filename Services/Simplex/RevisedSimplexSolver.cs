using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OptiSolver.NET.Core;
using OptiSolver.NET.Services.Base;

namespace OptiSolver.NET.Services.Simplex
{
    /// <summary>
    /// Revised Simplex (Two-Phase) with iteration logging and sensitivity artifacts.
    /// </summary>
    public sealed class RevisedSimplexSolver : SolverBase
    {
        public override string AlgorithmName => "Revised Simplex (Two-Phase)";
        public override string Description =>
            "Revised simplex supporting Phase I on artificials and Phase II on the original objective. " +
            "Logs iterations and returns basis/B^-1/reduced costs/duals for sensitivity.";

        private const double EPS = 1e-10;

        // Options
        private bool _useBlandsRule = false;
        private int _maxIterations = 2000;

        // Canonical model
        private CanonicalForm _canonical = null;

        // Basis state
        private List<int> _Bidx;     // basis column indices (size m)
        private double[,] _B;        // basis matrix (m x m)
        private double[,] _BInv;     // inverse of B (m x m)

        // Logging
        private StringBuilder _log;

        public override SolutionResult Solve(LPModel model, Dictionary<string, object> options = null)
        {
            var err = ValidateModel(model);
            if (err != null)
                return err;

            var started = DateTime.UtcNow;

            try
            {
                // Options
                var merged = MergeOptions(options);
                if (merged.ContainsKey("BlandsRule"))
                    _useBlandsRule = (bool)merged["BlandsRule"];
                if (merged.ContainsKey("MaxIterations"))
                    _maxIterations = (int)merged["MaxIterations"];

                // Canonicalize model
                _canonical = new ModelCanonicalTransformer().Canonicalize(model);
                int m = _canonical.ConstraintCount;
                int n = _canonical.TotalVariables;

                _log = new StringBuilder();
                LogHeader("=== REVISED SIMPLEX: CANONICAL FORM ===");
                _log.AppendLine($"m={m}, n={n}, Objective={_canonical.ObjectiveType}, RequiresPhaseI={_canonical.RequiresPhaseI}");

                // Initial basis from canonical A (unit columns first; artificial fallback)
                PrepareInitialBasis();

                // ---------------- Phase I (feasibility) ----------------
                if (_canonical.RequiresPhaseI)
                {
                    LogHeader("=== PHASE I (min sum of artificials) ===");
                    var cPhaseI = BuildPhaseICostVector();

                    try
                    {
                        var phaseI = RunSimplex(cPhaseI, minimize: true);

                        LogLine($"Phase I objective: {Round3(phaseI.ObjectiveValue)}");
                        if (phaseI.ObjectiveValue > 1e-8)
                        {
                            var infeas = SolutionResult.CreateInfeasible(
                                algorithm: AlgorithmName,
                                message: "Problem infeasible: Phase I objective > 0."
                            );
                            infeas.Info["IterationLog"] = _log.ToString();
                            infeas.Info["BasisIndices"] = _Bidx?.ToArray();
                            return infeas;
                        }
                        // If artificials remain in basis at zero, Phase II will pivot them out.
                    }
                    catch (InvalidOperationException ex) when (ex.Message.StartsWith("Unbounded"))
                    {
                        var r = SolutionResult.CreateUnbounded(AlgorithmName, "LP relaxation is unbounded.");
                        r.Info["IterationLog"] = _log.ToString();
                        return r;
                    }
                    catch (InvalidOperationException ex) when (ex.Message.StartsWith("Exceeded maximum iterations"))
                    {
                        var r = SolutionResult.CreateError(AlgorithmName, ex.Message);
                        r.Info["IterationLog"] = _log.ToString();
                        return r;
                    }
                }

                // ---------------- Phase II (optimize original c) ----------------
                LogHeader("=== PHASE II (optimize original objective) ===");
                bool minimize = _canonical.ObjectiveType == ObjectiveType.Minimize;
                var cPhaseII = (double[])_canonical.ObjectiveCoefficients.Clone();

                try
                {
                    var phaseII = RunSimplex(cPhaseII, minimize);

                    // Build final solution in original variable space
                    var xCanon = phaseII.CanonicalSolution;
                    var xOriginal = _canonical.VariableMapping.GetOriginalSolution(xCanon);

                    // Objective in user-sense: c^T x (no flips)
                    double objective = Dot(_canonical.ObjectiveCoefficients, xCanon);

                    var result = SolutionResult.CreateOptimal(
                        objectiveValue: objective,
                        variableValues: xOriginal,
                        iterations: phaseII.Iterations,
                        algorithm: AlgorithmName,
                        solveTimeMs: (DateTime.UtcNow - started).TotalMilliseconds,
                        message: phaseII.HasAlternateOptima ? "Optimal (alternate optima detected)." : "Optimal solution found."
                    );

                    // Sensitivity artifacts + full log
                    result.ReducedCosts = phaseII.ReducedCosts;
                    result.HasAlternateOptima = phaseII.HasAlternateOptima;
                    result.Info["BasisIndices"] = _Bidx.ToArray();                   // int[]
                    result.Info["BInv"] = _BInv;                                    // double[,]
                    var yDual = ComputeDual(cPhaseII, _Bidx, _BInv);
                    result.Info["DualY"] = yDual;
                    result.DualValues = (double[])yDual.Clone();
                    result.Info["IterationLog"] = _log.ToString();
                    return result;
                }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith("Unbounded"))
                {
                    var r = SolutionResult.CreateUnbounded(AlgorithmName, "LP relaxation is unbounded.");
                    r.Info["IterationLog"] = _log.ToString();
                    return r;
                }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith("Exceeded maximum iterations"))
                {
                    var r = SolutionResult.CreateError(AlgorithmName, ex.Message);
                    r.Info["IterationLog"] = _log.ToString();
                    return r;
                }

            }
            catch (Exception ex)
            {
                return SolutionResult.CreateError(AlgorithmName, ex.Message);
            }
        }

        // ---------------- Core revised simplex loop ----------------

        private (double ObjectiveValue, double[] CanonicalSolution, double[] ReducedCosts, int Iterations, bool HasAlternateOptima)
            RunSimplex(double[] c, bool minimize)
        {
            int m = _canonical.ConstraintCount;
            int n = _canonical.TotalVariables;

            int iter = 0;
            while (true)
            {
                if (iter >= _maxIterations)
                    throw new InvalidOperationException($"Exceeded maximum iterations ({_maxIterations}).");

                iter++;

                // y^T = c_B^T B^{-1}
                var yT = ComputeDual(c, _Bidx, _BInv);

                // Reduced costs r_j = c_j - y^T A_j
                var r = new double[n];
                double minR = double.PositiveInfinity, maxR = double.NegativeInfinity;
                for (int j = 0; j < n; j++)
                {
                    double yTAj = 0.0;
                    for (int i = 0; i < m; i++)
                        yTAj += yT[i] * _canonical.ConstraintMatrix[i, j];
                    r[j] = c[j] - yTAj;
                    if (r[j] < minR)
                        minR = r[j];
                    if (r[j] > maxR)
                        maxR = r[j];
                }

                // Entering var by improvement rule
                int entering = SelectEntering(r, minimize);

                // Optimality
                if (entering == -1)
                {
                    // x_B = B^{-1} b ; expand to canonical x
                    var xB = MultiplyMatrixByCol(_BInv, _canonical.RightHandSide);
                    var x = new double[n];
                    for (int i = 0; i < m; i++)
                        x[_Bidx[i]] = Math.Max(0.0, xB[i]);

                    double obj = Dot(c, x);
                    bool alt = HasAlternateOptima(r, _Bidx);

                    LogLine($"[Iter {iter}] Optimal. Obj={Round3(obj)}  min(r)={Round3(minR)} max(r)={Round3(maxR)}  y≈[{Preview(yT)}]");
                    return (obj, x, r, iter, alt);
                }

                // Direction d = B^{-1} * A_entering
                var a_e = new double[m];
                for (int i = 0; i < m; i++)
                    a_e[i] = _canonical.ConstraintMatrix[i, entering];
                var d = MultiplyMatrixByCol(_BInv, a_e);

                // Ratio test on xB / d
                var xB_cur = MultiplyMatrixByCol(_BInv, _canonical.RightHandSide);

                int leaving = -1;
                double minRatio = double.PositiveInfinity;
                for (int i = 0; i < m; i++)
                {
                    if (d[i] > EPS)
                    {
                        double ratio = xB_cur[i] / d[i];
                        if (ratio >= -EPS && ratio < minRatio - EPS)
                        {
                            minRatio = ratio;
                            leaving = i;
                        }
                        else if (_useBlandsRule && Math.Abs(ratio - minRatio) < EPS && leaving != -1)
                        {
                            // Bland tie-break: smallest row index
                            leaving = Math.Min(leaving, i);
                        }
                    }
                }
                if (leaving == -1)
                    throw new InvalidOperationException("Unbounded: no valid leaving basic variable.");

                LogLine($"[Iter {iter}] Enter j={entering + 1}, Leave row={leaving + 1}  min(r)={Round3(minR)} max(r)={Round3(maxR)}  y≈[{Preview(yT)}]");

                // Update basis: replace Bidx[leaving] with entering and rebuild B & B^{-1}
                _Bidx[leaving] = entering;
                RebuildBasis();
            }
        }

        // ---------------- Setup & utilities ----------------

        private void PrepareInitialBasis()
        {
            int m = _canonical.ConstraintCount;
            int n = _canonical.TotalVariables;
            var A = _canonical.ConstraintMatrix;
            var arts = (IEnumerable<int>)_canonical.ArtificialVariableIndices ?? Array.Empty<int>();
            var artSet = new HashSet<int>(arts);

            _Bidx = Enumerable.Repeat(-1, m).ToList();

            // 1) Prefer true unit columns (slacks) — classic identity basis
            for (int i = 0; i < m; i++)
            {
                int unitCol = FindUnitColumnAtRow(A, i, m, n);
                if (unitCol >= 0)
                {
                    _Bidx[i] = unitCol;
                }
            }

            // 2) For rows still without a basis var, try an artificial column for that row
            for (int i = 0; i < m; i++)
            {
                if (_Bidx[i] >= 0)
                    continue;

                int artiCol = FindArtificialColumnForRow(A, i, m, n, artSet);
                if (artiCol >= 0 && !_Bidx.Contains(artiCol))
                {
                    _Bidx[i] = artiCol;
                }
            }

            // 3) Final sanity: if any row still lacks a basis, pick a reasonable column that
            // makes B invertible (greedy). This is a rare fallback, but avoids hard failure.
            for (int i = 0; i < m; i++)
            {
                if (_Bidx[i] >= 0)
                    continue;

                int picked = -1;
                double bestAbs = 0.0;
                for (int j = 0; j < n; j++)
                {
                    if (_Bidx.Contains(j))
                        continue;
                    double aij = A[i, j];
                    if (Math.Abs(aij) > bestAbs + 1e-14)
                    {
                        bestAbs = Math.Abs(aij);
                        picked = j;
                    }
                }
                if (picked >= 0)
                    _Bidx[i] = picked;
            }

            // If still some -1 (pathological), throw a clear message
            if (_Bidx.Any(k => k < 0))
                throw new InvalidOperationException("Could not construct an initial basis: missing identity/artificial columns.");

            RebuildBasis();
        }

        private static int FindUnitColumnAtRow(double[,] A, int row, int m, int n)
        {
            for (int j = 0; j < n; j++)
            {
                if (!Approximately(A[row, j], 1.0))
                    continue;

                bool ok = true;
                for (int i = 0; i < m; i++)
                {
                    if (i == row)
                        continue;
                    if (!Approximately(A[i, j], 0.0))
                    { ok = false; break; }
                }
                if (ok)
                    return j;
            }
            return -1;
        }

        private static int FindArtificialColumnForRow(double[,] A, int row, int m, int n, HashSet<int> artSet)
        {
            if (artSet == null || artSet.Count == 0)
                return -1;

            foreach (var j in artSet)
            {
                if (j < 0 || j >= n)
                    continue;
                if (!Approximately(A[row, j], 1.0))
                    continue;

                bool ok = true;
                for (int i = 0; i < m; i++)
                {
                    if (i == row)
                        continue;
                    if (!Approximately(A[i, j], 0.0))
                    { ok = false; break; }
                }
                if (ok)
                    return j;
            }
            return -1;
        }

        private static bool Approximately(double a, double b, double tol = EPS) => Math.Abs(a - b) < tol;

        private void RebuildBasis()
        {
            int m = _canonical.ConstraintCount;
            _B = new double[m, m];
            for (int i = 0; i < m; i++)
            {
                int colIdx = _Bidx[i];
                for (int r = 0; r < m; r++)
                    _B[r, i] = _canonical.ConstraintMatrix[r, colIdx]; // B columns are A[:, basis]
            }
            _BInv = InvertWithPartialPivoting(_B);
        }

        private double[] BuildPhaseICostVector()
        {
            int n = _canonical.TotalVariables;
            var c = new double[n];
            foreach (var j in _canonical.ArtificialVariableIndices)
                c[j] = 1.0; // minimize sum of artificials
            return c;
        }

        private int SelectEntering(double[] r, bool minimize)
        {
            int n = r.Length;
            int entering = -1;

            if (minimize)
            {
                // any r_j < 0 improves
                double best = 0.0;
                for (int j = 0; j < n; j++)
                {
                    if (r[j] < best - EPS)
                    {
                        best = r[j];
                        entering = j;
                        if (_useBlandsRule)
                            break; // Bland's: first improving
                    }
                }
            }
            else
            {
                // maximize: any r_j > 0 improves
                double best = 0.0;
                for (int j = 0; j < n; j++)
                {
                    if (r[j] > best + EPS)
                    {
                        best = r[j];
                        entering = j;
                        if (_useBlandsRule)
                            break; // Bland's: first improving
                    }
                }
            }
            return entering;
        }

        private static bool HasAlternateOptima(double[] r, List<int> basis)
        {
            var basic = new HashSet<int>(basis);
            // zero reduced cost on any nonbasic at optimal
            return Enumerable.Range(0, r.Length).Any(j => !basic.Contains(j) && Math.Abs(r[j]) < 1e-9);
        }

        private static double[] ComputeDual(double[] c, List<int> Bidx, double[,] BInv)
        {
            int m = BInv.GetLength(0);
            var cB = new double[m];
            for (int i = 0; i < m; i++)
                cB[i] = c[Bidx[i]];
            return MultiplyRowByMatrix(cB, BInv); // y^T
        }

        private static double[] MultiplyRowByMatrix(double[] row, double[,] mat)
        {
            int r = row.Length;
            int k = mat.GetLength(1);
            var res = new double[k];
            for (int j = 0; j < k; j++)
                for (int i = 0; i < r; i++)
                    res[j] += row[i] * mat[i, j];
            return res;
        }

        private static double[] MultiplyMatrixByCol(double[,] mat, double[] col)
        {
            int r = mat.GetLength(0);
            int c = mat.GetLength(1);
            var res = new double[r];
            for (int i = 0; i < r; i++)
                for (int j = 0; j < c; j++)
                    res[i] += mat[i, j] * col[j];
            return res;
        }

        private static double Dot(double[] a, double[] b)
        {
            double s = 0.0;
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
                s += a[i] * b[i];
            return s;
        }

        // -------- Numeric: inverse with partial pivoting --------

        private static double[,] InvertWithPartialPivoting(double[,] A)
        {
            int n = A.GetLength(0);
            var aug = new double[n, 2 * n];

            // [A | I]
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                    aug[i, j] = A[i, j];
                aug[i, n + i] = 1.0;
            }

            for (int col = 0; col < n; col++)
            {
                // pivot row
                int piv = col;
                double best = Math.Abs(aug[piv, col]);
                for (int r = col + 1; r < n; r++)
                {
                    double v = Math.Abs(aug[r, col]);
                    if (v > best)
                    { best = v; piv = r; }
                }
                if (best < 1e-14)
                    throw new InvalidOperationException("Singular basis matrix.");

                // swap
                if (piv != col)
                    SwapRows(aug, piv, col);

                // normalize pivot row
                double diag = aug[col, col];
                for (int j = 0; j < 2 * n; j++)
                    aug[col, j] /= diag;

                // eliminate elsewhere
                for (int r = 0; r < n; r++)
                {
                    if (r == col)
                        continue;
                    double f = aug[r, col];
                    if (Math.Abs(f) < 1e-16)
                        continue;
                    for (int j = 0; j < 2 * n; j++)
                        aug[r, j] -= f * aug[col, j];
                }
            }

            var inv = new double[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    inv[i, j] = Math.Abs(aug[i, n + j]) < 1e-12 ? 0.0 : aug[i, n + j];

            return inv;
        }

        private static void SwapRows(double[,] M, int r1, int r2)
        {
            if (r1 == r2)
                return;
            int cols = M.GetLength(1);
            for (int j = 0; j < cols; j++)
            {
                double tmp = M[r1, j];
                M[r1, j] = M[r2, j];
                M[r2, j] = tmp;
            }
        }

        // ---------------- Logging helpers ----------------
        private void LogHeader(string title)
        {
            _log.AppendLine();
            _log.AppendLine(title);
            _log.AppendLine(new string('=', title.Length));
        }

        private void LogLine(string s) => _log.AppendLine(s);

        private static string Round3(double v) => v.ToString("0.000");

        private static string Preview(double[] v, int k = 5)
        {
            if (v == null)
                return "";
            k = Math.Min(k, v.Length);
            var head = string.Join(", ", v.Take(k).Select(Round3));
            return v.Length > k ? head + ", ..." : head;
        }
    }
}
