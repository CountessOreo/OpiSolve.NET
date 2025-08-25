using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OptiSolver.NET.Core;
using OptiSolver.NET.Services.Base;

namespace OptiSolver.NET.Services.Simplex
{
    /// <summary>
    /// Primal Simplex (tableau) with Two-Phase method, iteration logging and SolutionResult output.
    /// Implements both Phase I (artificial variables) and Phase II optimization.
    /// </summary>
    public sealed class PrimalSimplexTableauSolver : SolverBase
    {
        public override string AlgorithmName => "Primal Simplex (Tableau)";
        public override string Description =>
            "Two-Phase Primal Simplex using tableau method. Handles artificial variables via Phase I, " +
            "logs all iterations with 3 decimal precision, and supports sensitivity analysis.";

        private double[,] _tab;
        private int _m, _n;
        private List<int> _basis;
        private CanonicalForm _canonical;
        private StringBuilder _log;
        private bool _useBlandsRule = false; // anti-cycling
        private int _maxIterations = 1000;
        private List<int> _artificialIndices; // Track artificial variables for Phase I
        private int[] _columnMap;

        /// <summary>
        /// Solves the linear programming model using Two-Phase Primal Simplex method
        /// </summary>
        public override SolutionResult Solve(LPModel model, Dictionary<string, object> options = null)
        {
            var started = DateTime.UtcNow;
            var merged = MergeOptions(options);

            // Extract options
            if (merged.ContainsKey("BlandsRule"))
                _useBlandsRule = (bool)merged["BlandsRule"];
            if (merged.ContainsKey("MaxIterations"))
                _maxIterations = (int)merged["MaxIterations"];

            // Validate model
            var err = ValidateModel(model);
            if (err != null)
                return err;

            try
            {
                _log = new StringBuilder();
                _canonical = new ModelCanonicalTransformer().Canonicalize(model);
                _m = _canonical.ConstraintCount;
                _n = _canonical.TotalVariables;
                _artificialIndices = new List<int>();

                LogHeader("=== CANONICAL FORM ===");
                LogCanonicalForm();

                // Phase I if artificial variables are needed
                if (_canonical.RequiresPhaseI)
                {
                    var phaseIResult = SolvePhaseI();
                    if (phaseIResult != null) // Infeasible or error
                        return phaseIResult;
                }
                else
                {
                    // Phase II only path
                    PrepareTableauPhaseIIOnly();
                    _basis = DetectInitialBasisIdentityColumns();
                    MakeObjectiveRowCanonicalUsingBasis();
                    LogLine("Objective row canonicalised against initial basis.");
                    LogTableau();
                }

                // Phase II - Main optimization
                var phaseIIResult = SolvePhaseII();

                // Add final information to result
                phaseIIResult.Info["FinalTableau"] = SnapshotTableau();
                phaseIIResult.Info["BasisIndices"] = _basis.ToArray();
                phaseIIResult.Info["Log"] = _log.ToString();
                phaseIIResult.Info["CanonicalVariableCount"] = _n;
                phaseIIResult.Info["OriginalVariableCount"] = _canonical.OriginalVariableCount;

                return phaseIIResult;
            }
            catch (Exception ex)
            {
                return SolutionResult.CreateError(AlgorithmName, $"Unexpected error: {ex.Message}");
            }
        }

        #region Phase I Implementation
        private SolutionResult SolvePhaseI()
        {
            LogHeader("=== PHASE I: FINDING INITIAL FEASIBLE SOLUTION ===");

            PreparePhaseITableau();
            _basis = DetectInitialBasisIdentityColumns();

            int iterations = 0;
            while (iterations < _maxIterations)
            {
                iterations++;

                int entering = FindEnteringColumnPhaseI();
                if (IsOptimalPhaseI(entering))
                {
                    LogLine($"[Phase I - Iter {iterations}] Phase I optimality reached.");
                    break;
                }

                int leavingRow = FindLeavingRow(entering);
                if (leavingRow == -1)
                {
                    LogLine($"[Phase I - Iter {iterations}] Phase I unbounded (this shouldn't happen).");
                    return SolutionResult.CreateError(AlgorithmName, "Phase I unbounded - model setup error.");
                }

                LogPivot(iterations, leavingRow, entering, "Phase I");
                Pivot(leavingRow, entering);
                _basis[leavingRow - 1] = entering;
            }

            if (iterations >= _maxIterations)
            {
                return SolutionResult.CreateError(AlgorithmName, "Phase I exceeded maximum iterations.");
            }

            // Check if Phase I found a feasible solution
            double phaseIObjective = _tab[0, _n];
            LogLine($"Phase I objective value: {Round3(phaseIObjective)}");

            if (Math.Abs(phaseIObjective) > EPSILON)
            {
                LogLine("INFEASIBLE: Phase I objective > 0, no feasible solution exists.");
                return SolutionResult.CreateInfeasible(AlgorithmName,
                    "Problem is infeasible - Phase I could not eliminate artificial variables.");
            }

            // Remove artificial variables and prepare for Phase II
            RemoveArtificialVariables();

            // NEW: after removing artificials, verify/repair basis and eliminate basic columns from row 0
            EnsureInitialBasisPhaseIIOnly();
            EliminateObjectiveBasicColumns();

            LogHeader("=== PHASE I COMPLETE: TRANSITIONING TO PHASE II ===");
            return null; // Continue to Phase II
        }

        private void PreparePhaseITableau()
        {
            // Create tableau with artificial variables
            _tab = new double[_m + 1, _n + 1];

            // Fill constraints
            for (int i = 0; i < _m; i++)
            {
                for (int j = 0; j < _n; j++)
                    _tab[i + 1, j] = _canonical.ConstraintMatrix[i, j];
                _tab[i + 1, _n] = _canonical.RightHandSide[i];
            }

            // Phase I objective: minimize sum of artificial variables
            for (int j = 0; j < _n; j++)
            {
                if (_canonical.IsArtificialVariable(j))
                {
                    _tab[0, j] = -1.0; // Minimize artificial variables
                    _artificialIndices.Add(j);
                }
                else
                {
                    _tab[0, j] = 0.0;
                }
            }
            _tab[0, _n] = 0.0;

            // Make Phase I objective feasible by eliminating artificial variables from row 0
            foreach (int artIdx in _artificialIndices)
            {
                for (int i = 1; i <= _m; i++)
                {
                    if (Math.Abs(_tab[i, artIdx] - 1.0) < EPSILON)
                    {
                        for (int j = 0; j <= _n; j++)
                            _tab[0, j] += _tab[i, j];
                        break;
                    }
                }
            }

            LogLine("Initial Phase I tableau prepared.");
            LogTableau();
        }

        private int FindEnteringColumnPhaseI() => FindEnteringColumnGeneric();
        private bool IsOptimalPhaseI(int entering) => entering == -1;

        private void RemoveArtificialVariables()
        {
            // Create new tableau without artificial variable columns
            int newN = _n - _artificialIndices.Count;
            double[,] newTab = new double[_m + 1, newN + 1];
            var newMap = new int[newN];  // <-- map from new j -> old J0

            int newJ = 0;
            for (int j = 0; j < _n; j++)
            {
                if (!_artificialIndices.Contains(j))
                {
                    // Copy non-artificial columns
                    for (int i = 0; i <= _m; i++)
                        newTab[i, newJ] = _tab[i, j];

                    // Update basis indices
                    for (int k = 0; k < _basis.Count; k++)
                    {
                        if (_basis[k] == j)
                            _basis[k] = newJ;
                        else if (_basis[k] > j)
                            _basis[k]--; // Shift down due to removed columns
                    }

                    // Record where this column came from in the original canonical space
                    newMap[newJ] = j;

                    newJ++;
                }
            }

            // Copy RHS
            for (int i = 0; i <= _m; i++)
                newTab[i, newN] = _tab[i, _n];

            // Swap in new tableau + sizes + column map
            _tab = newTab;
            _n = newN;
            _columnMap = newMap;

            // Setup Phase II objective in row 0, using mapped canonical coefficients
            double sign = _canonical.ObjectiveType == ObjectiveType.Maximize ? -1.0 : 1.0;
            for (int j = 0; j < _n; j++)
                _tab[0, j] = sign * GetOriginalObjectiveCoefficient(j);

            _tab[0, _n] = 0.0;

            LogLine("Artificial variables removed. Preliminary Phase II objective row set.");
            LogTableau();
        }

        #endregion

        #region Phase II Implementation
        private SolutionResult SolvePhaseII()
        {
            var started = DateTime.UtcNow;
            LogHeader("=== PHASE II: OPTIMIZATION ===");

            int iterations = 0;
            while (iterations < _maxIterations)
            {
                iterations++;

                int entering = FindEnteringColumnPhaseII();
                if (IsOptimalPhaseII(entering))
                {
                    LogLine($"[Phase II - Iter {iterations}] Optimality reached.");
                    break;
                }

                int leavingRow = FindLeavingRow(entering);
                if (leavingRow == -1)
                {
                    LogLine($"[Phase II - Iter {iterations}] Unbounded: no valid pivot in column {entering + 1}.");
                    return SolutionResult.CreateUnbounded(AlgorithmName,
                        "Problem is unbounded - no valid leaving variable for chosen entering column.");
                }

                LogPivot(iterations, leavingRow, entering, "Phase II");
                Pivot(leavingRow, entering);
                _basis[leavingRow - 1] = entering;

                // Check for degeneracy
                if (Math.Abs(_tab[leavingRow, _n]) < EPSILON)
                    LogLine($"    [Degeneracy detected at iteration {iterations}]");
            }

            if (iterations >= _maxIterations)
                return SolutionResult.CreateError(AlgorithmName, "Phase II exceeded maximum iterations.");

            // Extract solution
            var xCanonCurrent = ExtractCanonicalSolution();
            var xCanonFull = RemapCurrentToCanonical(xCanonCurrent);
            var xOriginal = _canonical.VariableMapping.GetOriginalSolution(xCanonFull);

            // Calculate objective value
            double value = (_canonical.ObjectiveType == ObjectiveType.Maximize)
                ? -_tab[0, _n]
                : _tab[0, _n];

            var reducedCosts = GetReducedCostsRow();

            // Check alternate optima (zero reduced costs on nonbasic)
            bool hasAlternateOptima = CheckForAlternateOptima(reducedCosts);
            string message = hasAlternateOptima
                ? "Optimal solution found. Alternate optima detected."
                : "Optimal solution found.";

            var result = SolutionResult.CreateOptimal(
                objectiveValue: value,
                variableValues: xOriginal,
                iterations: iterations,
                algorithm: AlgorithmName,
                solveTimeMs: (DateTime.UtcNow - started).TotalMilliseconds,
                message: message
            );

            // These properties assume your SolutionResult supports them
            result.ReducedCosts = reducedCosts;
            result.HasAlternateOptima = hasAlternateOptima;

            LogLine($"\nFINAL SOLUTION:");
            LogLine($"Objective Value: {Round3(value)}");
            LogLine($"Iterations: {iterations}");
            LogLine($"Variables: [{string.Join(", ", xOriginal.Select(x => Round3(x)))}]");
            if (hasAlternateOptima)
                LogLine("Alternate optima detected (zero reduced costs for non-basic variables).");

            return result;
        }

        private int FindEnteringColumnPhaseII() => FindEnteringColumnGeneric();
        private bool IsOptimalPhaseII(int entering) => entering == -1;

        private bool CheckForAlternateOptima(double[] reducedCosts)
        {
            for (int j = 0; j < _n; j++)
                if (!IsBasicVariable(j) && Math.Abs(reducedCosts[j]) < EPSILON)
                    return true;
            return false;
        }

        private bool IsBasicVariable(int col) => _basis.Contains(col);
        #endregion

        #region Tableau Setup & Basis Handling (Phase II only)
        private void PrepareTableauPhaseIIOnly()
        {
            _tab = new double[_m + 1, _n + 1];
            _columnMap = Enumerable.Range(0, _n).ToArray();

            // Fill constraints
            for (int i = 0; i < _m; i++)
            {
                for (int j = 0; j < _n; j++)
                    _tab[i + 1, j] = _canonical.ConstraintMatrix[i, j];
                _tab[i + 1, _n] = _canonical.RightHandSide[i];
            }

            // Objective row: sign * c (do NOT eliminate basics yet)
            double sign = _canonical.ObjectiveType == ObjectiveType.Maximize ? -1.0 : 1.0;
            for (int j = 0; j < _n; j++)
                _tab[0, j] = sign * GetOriginalObjectiveCoefficient(j);
            _tab[0, _n] = 0.0;

            LogHeader("Initial tableau (Phase II only, raw objective)");
            LogTableau();
        }

        /// <summary>
        /// Ensures an initial basis exists. If identity columns aren't found after canonicalization,
        /// tries to repair by greedy pivoting to form unit columns row-by-row.
        /// </summary>
        private void EnsureInitialBasisPhaseIIOnly()
        {
            _basis = DetectInitialBasisIdentityColumns();

            // If any row lacks a basic column, attempt to pivot a usable column into unit form
            bool needsRepair = _basis.Any(b => b < 0);
            if (!needsRepair)
                return;

            LogLine("Initial identity basis not fully present. Attempting greedy basis repair...");

            for (int r = 0; r < _m; r++)
            {
                if (_basis[r] >= 0)
                    continue;

                // Find a column that can serve as a pivot for row r: prefer columns with a nonzero at row r
                // and small/near-zero elsewhere. We'll pivot on (r+1, j) then eliminate the column from other rows.
                int bestCol = -1;
                double bestScore = double.PositiveInfinity;

                for (int j = 0; j < _n; j++)
                {
                    if (_basis.Contains(j))
                        continue; // already basic elsewhere

                    double a_rj = _tab[r + 1, j];
                    if (Math.Abs(a_rj) < EPSILON)
                        continue;

                    // score = sum of absolute values in column excluding row r (smaller is better)
                    double colSumOthers = 0.0;
                    for (int i = 0; i < _m; i++)
                    {
                        if (i == r)
                            continue;
                        colSumOthers += Math.Abs(_tab[i + 1, j]);
                    }

                    // Prefer columns that are close to unit columns already
                    double score = colSumOthers + Math.Abs(a_rj - 1.0);
                    if (score < bestScore - 1e-15)
                    {
                        bestScore = score;
                        bestCol = j;
                    }
                }

                if (bestCol >= 0)
                {
                    // Perform a pivot to make column bestCol the unit vector at row r
                    int pivotRow = r + 1;
                    if (Math.Abs(_tab[pivotRow, bestCol]) < EPSILON)
                        continue;

                    // Normalize pivot row
                    double piv = _tab[pivotRow, bestCol];
                    for (int jj = 0; jj <= _n; jj++)
                        _tab[pivotRow, jj] /= piv;

                    // Eliminate from other rows, including objective row
                    for (int ii = 0; ii <= _m; ii++)
                    {
                        if (ii == pivotRow)
                            continue;
                        double factor = _tab[ii, bestCol];
                        if (Math.Abs(factor) < EPSILON)
                            continue;
                        for (int jj = 0; jj <= _n; jj++)
                            _tab[ii, jj] -= factor * _tab[pivotRow, jj];
                    }

                    _basis[r] = bestCol;
                }
            }

            LogLine($"Post-repair basis: [{string.Join(", ", _basis.Select(b => b >= 0 ? $"x{b + 1}" : "none"))}]");
            LogTableau();
        }

        /// <summary>
        /// Eliminates the current basic variable columns from the objective row (row 0).
        /// Must be called after _basis is correctly set.
        /// </summary>
        private void EliminateObjectiveBasicColumns()
        {
            if (_basis == null || _basis.Count != _m)
                return;

            for (int k = 0; k < _basis.Count; k++)
            {
                int basicCol = _basis[k];
                if (basicCol < 0 || basicCol >= _n)
                    continue;

                double coeff = _tab[0, basicCol];
                if (Math.Abs(coeff) > EPSILON)
                {
                    int row = k + 1;
                    for (int j = 0; j <= _n; j++)
                        _tab[0, j] -= coeff * _tab[row, j];
                }
            }

            LogLine("Objective row cleaned by eliminating basic columns (reduced costs ready).");
            LogTableau();
        }

        private void MakeObjectiveRowCanonicalUsingBasis()
        {
            if (_basis == null || _basis.Count == 0)
                return;

            for (int k = 0; k < _basis.Count; k++)
            {
                int basicCol = _basis[k];
                if (basicCol < 0 || basicCol >= _n)
                    continue;

                double coeff = _tab[0, basicCol];
                if (Math.Abs(coeff) < EPSILON)
                    continue;

                int row = k + 1; // tableau row for this basic var
                for (int j = 0; j <= _n; j++)
                    _tab[0, j] -= coeff * _tab[row, j];
            }
        }

        #endregion

        #region Column / Row Ops & Pivoting
        private List<int> DetectInitialBasisIdentityColumns()
        {
            var basis = new List<int>(_m);

            for (int i = 0; i < _m; i++)
            {
                int unitCol = -1;
                for (int j = 0; j < _n; j++)
                {
                    if (IsUnitColumnAtRow(j, i))
                    {
                        unitCol = j;
                        break;
                    }
                }
                basis.Add(unitCol);
            }

            LogLine($"Initial basis detected: [{string.Join(", ", basis.Select(b => b >= 0 ? $"x{b + 1}" : "none"))}]");
            return basis;
        }

        private bool IsUnitColumnAtRow(int col, int row)
        {
            if (!Approximately(_tab[row + 1, col], 1.0))
                return false;
            for (int i = 0; i < _m; i++)
            {
                if (i == row)
                    continue;
                if (!Approximately(_tab[i + 1, col], 0.0))
                    return false;
            }
            return true;
        }

        private int FindEnteringColumnGeneric()
        {
            int entering = -1;

            if (_canonical.ObjectiveType == ObjectiveType.Maximize)
            {
                if (_useBlandsRule)
                {
                    for (int j = 0; j < _n; j++)
                        if (_tab[0, j] < -EPSILON)
                        { entering = j; break; }
                }
                else
                {
                    double best = 0.0;
                    for (int j = 0; j < _n; j++)
                        if (_tab[0, j] < best - EPSILON)
                        { best = _tab[0, j]; entering = j; }
                }
            }
            else // Minimize
            {
                if (_useBlandsRule)
                {
                    for (int j = 0; j < _n; j++)
                        if (_tab[0, j] > EPSILON)
                        { entering = j; break; }
                }
                else
                {
                    double best = 0.0;
                    for (int j = 0; j < _n; j++)
                        if (_tab[0, j] > best + EPSILON)
                        { best = _tab[0, j]; entering = j; }
                }
            }

            return entering;
        }

        private int FindLeavingRow(int entering)
        {
            int leaving = -1;
            double minRatio = double.PositiveInfinity;

            for (int i = 1; i <= _m; i++)
            {
                double a = _tab[i, entering];
                if (a > EPSILON)
                {
                    double rhs = _tab[i, _n];
                    double ratio = rhs / a;
                    if (ratio >= -EPSILON)
                    {
                        if (ratio < minRatio - EPSILON)
                        {
                            minRatio = ratio;
                            leaving = i;
                        }
                        else if (Math.Abs(ratio - minRatio) < EPSILON && _useBlandsRule)
                        {
                            if (leaving == -1 || i < leaving)
                                leaving = i;
                        }
                    }
                }
            }
            return leaving;
        }

        private void Pivot(int pivotRow, int pivotCol)
        {
            double p = _tab[pivotRow, pivotCol];
            if (Math.Abs(p) < EPSILON)
                throw new InvalidOperationException($"Pivot element too small: {p}");

            // 1) Normalize the pivot row
            for (int j = 0; j <= _n; j++)
                _tab[pivotRow, j] /= p;

            // 2) Eliminate pivot column from all other rows (including objective row 0)
            for (int i = 0; i <= _m; i++)
            {
                if (i == pivotRow)
                    continue;
                double factor = _tab[i, pivotCol];
                if (Math.Abs(factor) < EPSILON)
                    continue;
                for (int j = 0; j <= _n; j++)
                    _tab[i, j] -= factor * _tab[pivotRow, j];
            }

            // 3) Cleanup tiny values
            for (int i = 0; i <= _m; i++)
                for (int j = 0; j <= _n; j++)
                    if (Math.Abs(_tab[i, j]) < 1e-12)
                        _tab[i, j] = 0.0;

            LogTableau();
        }

        #endregion

        #region Solution Extraction
        private double[] ExtractCanonicalSolution()
        {
            var x = new double[_n];
            for (int r = 1; r <= _m; r++)
            {
                int col = _basis[r - 1];
                if (col >= 0 && col < _n)
                    x[col] = Math.Max(0.0, _tab[r, _n]);
            }
            return x;
        }

        private double[] GetReducedCostsRow()
        {
            var r = new double[_n];
            for (int j = 0; j < _n; j++)
                r[j] = _tab[0, j];
            return r;
        }

        private double[,] SnapshotTableau()
        {
            var snapshot = new double[_m + 1, _n + 1];
            for (int i = 0; i <= _m; i++)
                for (int j = 0; j <= _n; j++)
                    snapshot[i, j] = _tab[i, j];
            return snapshot;
        }
        #endregion

        #region Logging
        private void LogHeader(string title)
        {
            _log.AppendLine();
            _log.AppendLine(title);
            _log.AppendLine(new string('=', title.Length));
        }

        private void LogLine(string s) => _log.AppendLine(s);

        private void LogCanonicalForm()
        {
            _log.AppendLine($"Variables: {_n} ({_canonical.OriginalVariableCount} original)");
            _log.AppendLine($"Constraints: {_m}");
            _log.AppendLine($"Objective: {_canonical.ObjectiveType}");
            _log.AppendLine($"Requires Phase I: {_canonical.RequiresPhaseI}");
            _log.AppendLine();
        }

        private void LogPivot(int iter, int leavingRow, int enteringCol, string phase)
        {
            var enterName = GetVariableName(enteringCol);
            var leaveName = _basis[leavingRow - 1] >= 0
                ? GetVariableName(_basis[leavingRow - 1])
                : "(none)";
            _log.AppendLine($"[{phase} - Iter {iter}] Entering: {enterName}, " +
                            $"Leaving: {leaveName}, " +
                            $"Pivot = {Round3(_tab[leavingRow, enteringCol])}");
        }

        private string GetVariableName(int index)
        {
            if (index < 0)
                return "(none)";

            // Map current tableau column -> original canonical index when available
            int canonicalIndex = (_columnMap != null && index < _columnMap.Length) ? _columnMap[index] : index;

            return _canonical?.VariableMapping?.GetCanonicalVariableName(canonicalIndex) ?? $"x{canonicalIndex + 1}";
        }


        private string Round3(double v) => v.ToString("0.000");

        private void LogTableau()
        {
            var headers = new List<string>();
            for (int j = 0; j < _n; j++)
                headers.Add(GetVariableName(j).PadLeft(8));
            headers.Add("RHS".PadLeft(8));

            _log.AppendLine(string.Join(" | ", headers));
            _log.AppendLine(new string('-', 10 * headers.Count));

            // Objective row
            var objRow = new List<string>();
            for (int j = 0; j <= _n; j++)
                objRow.Add(_tab[0, j].ToString("0.000").PadLeft(8));
            _log.AppendLine($"z    | {string.Join(" | ", objRow)}");

            // Constraint rows
            for (int i = 1; i <= _m; i++)
            {
                var row = new List<string>();
                for (int j = 0; j <= _n; j++)
                    row.Add(_tab[i, j].ToString("0.000").PadLeft(8));
                string basicVar = (_basis != null && i - 1 < _basis.Count && _basis[i - 1] >= 0)
                    ? GetVariableName(_basis[i - 1])
                    : "?";
                _log.AppendLine($"{basicVar.PadLeft(4)} | {string.Join(" | ", row)}");
            }
            _log.AppendLine();
        }
        #endregion

        #region Utilities
        private static bool Approximately(double a, double b, double tol = EPSILON) => Math.Abs(a - b) < tol;

        /// <summary>
        /// Map canonical index to the correct original objective coefficient.
        /// Falls back to canonical coefficients when mapping is unavailable.
        /// </summary>
        private double GetOriginalObjectiveCoefficient(int tableauColumnIndex)
        {
            if (_columnMap == null)
            {
                // identity mapping when columns haven’t been removed/reordered
                if (tableauColumnIndex >= 0 && tableauColumnIndex < _canonical.ObjectiveCoefficients.Length)
                    return _canonical.ObjectiveCoefficients[tableauColumnIndex];
                return 0.0;
            }

            // Guard
            if (_canonical?.ObjectiveCoefficients == null || _columnMap == null)
                return 0.0;

            if (tableauColumnIndex < 0 || tableauColumnIndex >= _columnMap.Length)
                return 0.0;

            int canonicalIndexBeforeRemovals = _columnMap[tableauColumnIndex];

            // Safety: ensure in range of the original canonical c-vector
            var cCanon = _canonical.ObjectiveCoefficients;
            if (canonicalIndexBeforeRemovals < 0 || canonicalIndexBeforeRemovals >= cCanon.Length)
                return 0.0;

            // Auxiliaries already have 0 in cCanon; but if you want to be explicit:
            var vm = _canonical.VariableMapping;
            if (vm != null && vm.IsAuxiliaryVariable(canonicalIndexBeforeRemovals))
                return 0.0;

            return cCanon[canonicalIndexBeforeRemovals];
        }

        /// <summary>
        /// Remap a vector defined over the current tableau columns (length = _n)
        /// back to the original canonical index space (length = vm.TotalCanonicalVariables).
        /// Places xCurrent[j] at canonical index _columnMap[j].
        /// </summary>
        private double[] RemapCurrentToCanonical(double[] xCurrent)
        {
            var vm = _canonical?.VariableMapping;
            if (vm == null || _columnMap == null)
                return xCurrent; // safest fallback

            int nCanon = vm.TotalCanonicalVariables;
            var xCanon = new double[nCanon]; // zeros by default

            int len = Math.Min(xCurrent.Length, _columnMap.Length);
            for (int j = 0; j < len; j++)
            {
                int J0 = _columnMap[j];
                if (J0 >= 0 && J0 < nCanon)
                    xCanon[J0] = xCurrent[j];
            }

            return xCanon;
        }


        #endregion
    }
}
