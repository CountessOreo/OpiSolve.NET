using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OptiSolver.NET.Services.BranchAndBound
{
    internal class BranchBoundSimplex
    {
        public class DualSimplexSolver
        {
            private List<int> pivotColumns;
            private List<int> pivotRows;
            private List<string> headerRow;

            private List<int> iterationPhases;

            public DualSimplexSolver()
            {
                pivotColumns = new List<int>();
                pivotRows = new List<int>();
                headerRow = new List<string>();
                iterationPhases = new List<int>();
            }

            public List<List<double>> FormulateTableau(List<double> objectiveFunction, List<List<double>> constraints)
            {
                int surplusCount = 0;
                int slackCount = 0;

                foreach (var constraint in constraints)
                {
                    if (constraint.Last() == 1)
                        surplusCount++;
                    else
                        slackCount++;
                }

                for (int i = 0; i < constraints.Count; i++)
                {
                    if (constraints[i].Last() == 1)
                    {
                        for (int j = 0; j < constraints[i].Count; j++)
                        {
                            constraints[i][j] = -1 * constraints[i][j];
                        }
                    }
                }

                for (int i = 0; i < constraints.Count; i++)
                {
                    constraints[i].RemoveAt(constraints[i].Count - 1);
                }

                int tableauHeight = constraints.Count + 1;
                int varCounter = 1;
                for (int i = 0; i < objectiveFunction.Count; i++)
                {
                    headerRow.Add($"y{varCounter++}");
                }

                varCounter = 1;
                if (surplusCount > 0)
                {
                    for (int i = 0; i < surplusCount; i++)
                    {
                        headerRow.Add($"u{varCounter++}");
                    }
                }

                if (slackCount > 0)
                {
                    for (int i = 0; i < slackCount; i++)
                    {
                        headerRow.Add($"t{varCounter++}");
                    }
                }

                headerRow.Add("solution");

                int tableauWidth = surplusCount + slackCount + 1 + objectiveFunction.Count;
                var tableau = new List<List<double>>();
                for (int i = 0; i < tableauHeight; i++)
                {
                    tableau.Add(new List<double>(new double[tableauWidth]));
                }

                for (int i = 0; i < objectiveFunction.Count; i++)
                {
                    tableau[0][i] = -objectiveFunction[i];
                }

                for (int i = 0; i < constraints.Count; i++)
                {
                    for (int j = 0; j < constraints[i].Count - 1; j++)
                    {
                        tableau[i + 1][j] = constraints[i][j];
                    }
                    tableau[i + 1][tableauWidth - 1] = constraints[i].Last();
                }

                for (int i = 1; i < tableau.Count; i++)
                {
                    for (int j = objectiveFunction.Count; j < tableau[i].Count - 1; j++)
                    {
                        tableau[i][i + objectiveFunction.Count - 1] = 1;
                    }
                }

                return tableau;
            }

            public (List<List<double>> updatedTableau, List<double> thetaValues) PerformDualPivot(List<List<double>> tableau)
            {
                var thetaValues = new List<double>();
                var rhsValues = tableau.Select(row => row.Last()).ToList();
                var negativeRhs = rhsValues.Where(x => x < 0).ToList();
                if (!negativeRhs.Any()) return (tableau, null);

                double minRhs = negativeRhs.Min();
                int pivotRowIndex = rhsValues.IndexOf(minRhs);
                var negativeColumns = new List<double>();

                for (int i = 0; i < tableau[pivotRowIndex].Count - 1; i++)
                {
                    negativeColumns.Add(tableau[pivotRowIndex][i] < 0 ? i : double.PositiveInfinity);
                }

                var dualThetas = new List<double>();
                for (int i = 0; i < negativeColumns.Count; i++)
                {
                    if (negativeColumns[i] != double.PositiveInfinity)
                    {
                        int columnIndex = (int)negativeColumns[i];
                        dualThetas.Add(Math.Abs(tableau[0][columnIndex] / tableau[pivotRowIndex][columnIndex]));
                    }
                    else
                    {
                        dualThetas.Add(double.PositiveInfinity);
                    }
                }

                thetaValues = dualThetas.ToList();
                double minPositiveTheta = dualThetas.All(x => x == 0 || x == double.PositiveInfinity)
                    ? 0
                    : dualThetas.Where(x => x > 0).DefaultIfEmpty(double.PositiveInfinity).Min();

                int rowIndex = pivotRowIndex;
                int colIndex;
                try
                {
                    colIndex = dualThetas.IndexOf(minPositiveTheta);
                }
                catch
                {
                    return (tableau, null);
                }

                var sourceTableau = tableau.Select(row => row.ToList()).ToList();
                var updatedTableau = sourceTableau.Select(row => new List<double>(new double[row.Count])).ToList();

                double pivotElement;
                try
                {
                    pivotElement = tableau[rowIndex][colIndex];
                }
                catch
                {
                    return (tableau, null);
                }

                for (int j = 0; j < sourceTableau[rowIndex].Count; j++)
                {
                    updatedTableau[rowIndex][j] = sourceTableau[rowIndex][j] / pivotElement;
                    if (updatedTableau[rowIndex][j] == -0.0) updatedTableau[rowIndex][j] = 0.0;
                }

                var pivotRow = updatedTableau[rowIndex].ToList();

                for (int i = 0; i < sourceTableau.Count; i++)
                {
                    if (i == rowIndex) continue;
                    for (int j = 0; j < sourceTableau[i].Count; j++)
                    {
                        double computedValue = sourceTableau[i][j] - (sourceTableau[i][colIndex] * updatedTableau[rowIndex][j]);
                        updatedTableau[i][j] = computedValue;
                    }
                }

                updatedTableau[rowIndex] = pivotRow;


                pivotColumns.Add(colIndex);
                pivotRows.Add(rowIndex);

                return (updatedTableau, thetaValues);
            }

            public (List<List<double>> updatedTableau, List<double> thetaValues) PerformPrimalPivot(List<List<double>> tableau, bool isMinimization)
            {
                var thetaValues = new List<double>();
                var objectiveRow = tableau[0].Take(tableau[0].Count - 1).ToList();
                double pivotValue;

                try
                {
                    pivotValue = isMinimization
                        ? objectiveRow.Where(num => num > 0 && num != 0).Min()
                        : objectiveRow.Where(num => num < 0 && num != 0).Min();
                }
                catch
                {
                    return (null, null);
                }

                int colIndex = tableau[0].IndexOf(pivotValue);
                var thetas = new List<double>();
                for (int i = 1; i < tableau.Count; i++)
                {
                    thetas.Add(tableau[i][colIndex] != 0 ? tableau[i].Last() / tableau[i][colIndex] : double.PositiveInfinity);
                }

                thetaValues = thetas.ToList();
                bool allThetasNegative = thetas.All(num => num < 0);

                if (allThetasNegative)
                    return (null, null);

                double minTheta;
                if (!thetas.Any(num => num > 0 && num != double.PositiveInfinity))
                {
                    if (thetas.Contains(0))
                        minTheta = 0.0;
                    else
                        return (null, null);
                }
                else
                {
                    minTheta = thetas.Where(x => x > 0 && x != double.PositiveInfinity).Min();
                }

                if (minTheta == double.PositiveInfinity && !thetas.Contains(0))
                    return (null, null);

                int rowIndex = thetas.IndexOf(minTheta) + 1;
                double pivotElement = tableau[rowIndex][colIndex];

                if (pivotElement == 0)
                    return (null, null);

                var updatedTableau = tableau.Select(row => new List<double>(new double[row.Count])).ToList();

                for (int j = 0; j < tableau[rowIndex].Count; j++)
                {
                    updatedTableau[rowIndex][j] = tableau[rowIndex][j] / pivotElement;
                    if (updatedTableau[rowIndex][j] == -0.0) updatedTableau[rowIndex][j] = 0.0;
                }

                for (int i = 0; i < tableau.Count; i++)
                {
                    if (i == rowIndex) continue;
                    for (int j = 0; j < tableau[i].Count; j++)
                    {
                        double computedValue = tableau[i][j] - (tableau[i][colIndex] * updatedTableau[rowIndex][j]);
                        updatedTableau[i][j] = computedValue;
                    }
                }

                pivotColumns.Add(colIndex);
                pivotRows.Add(rowIndex);

                return (updatedTableau, thetaValues);
            }

            public (List<List<double>> tableau, bool isMinimization, int surplusCount, int slackCount, int objectiveLength) PrepareInput(List<double> objectiveFunction, List<List<double>> constraints, bool isMinimization)
            {
                int surplusCount = constraints.Count(c => c.Last() == 1 || c.Last() == 2);
                int slackCount = constraints.Count(c => c.Last() != 1 && c.Last() != 2);
                var tableau = FormulateTableau(objectiveFunction, constraints);
                return (tableau, isMinimization, surplusCount, slackCount, objectiveFunction.Count);
            }

            public (List<List<List<double>>> tableaux, List<double> decisionVariables, double? optimalValue, List<int> pivotCols, List<int> pivotRows, List<string> headerRow) DoDualSimplex(List<double> objectiveFunction, List<List<double>> constraints, bool isMinimization, List<List<double>> tableauOverride = null)
            {
                var thetaColumns = new List<List<double>>();
                var tableaux = new List<List<List<double>>>();
                var (tableau, isMinLocal, surplusCount, slackCount, objectiveLength) = PrepareInput(objectiveFunction, constraints, isMinimization);

                if (tableauOverride != null)
                {
                    tableau = tableauOverride;
                    pivotColumns = new List<int>();
                    pivotRows = new List<int>();
                    headerRow.RemoveAt(headerRow.Count - 1);
                }

                tableaux.Add(tableau);

                while (true)
                {
                    foreach (var row in tableaux.Last())
                    {
                        for (int i = 0; i < row.Count; i++)
                        {
                            if (row[i] == -0.0) row[i] = 0.0;
                        }
                    }

                    var rhsValues = tableaux.Last().Select(row => row.Last()).ToList();
                    const double epsilon = 1e-9;
                    bool allRhsNonNegative = rhsValues.All(num => num >= -epsilon);

                    if (allRhsNonNegative)
                        break;

                    var (newTableau, thetaRow) = PerformDualPivot(tableaux.Last());

                    if (thetaRow == null)
                    {
                        if (tableauOverride == null)
                        {
                            return (tableaux, null, null, null, null, null);
                        }
                        return (tableaux, null, null, null, null, null);
                    }

                    foreach (var row in newTableau)
                    {
                        for (int i = 0; i < row.Count; i++)
                        {
                            if (row[i] == -0.0) row[i] = 0.0;
                        }
                    }

                    tableaux.Add(newTableau);
                    iterationPhases.Add(0);
                }

                var objectiveRowTest = tableaux.Last()[0].Take(tableaux.Last()[0].Count - 1).ToList();
                bool isOptimal = isMinLocal
                    ? objectiveRowTest.All(num => num <= 0)
                    : objectiveRowTest.All(num => num >= 0);

                if (!isOptimal)
                {
                    while (true)
                    {
                        foreach (var row in tableaux.Last())
                        {
                            for (int i = 0; i < row.Count; i++)
                            {
                                if (row[i] == -0.0) row[i] = 0.0;
                            }
                        }

                        if (tableaux.Last() == null)
                        {
                            break;
                        }

                        objectiveRowTest = tableaux.Last()[0].Take(tableaux.Last()[0].Count - 1).ToList();
                        isOptimal = isMinLocal
                            ? objectiveRowTest.All(num => num <= 0)
                            : objectiveRowTest.All(num => num >= 0);

                        if (isOptimal)
                            break;

                        var (newTableau, thetaCol) = PerformPrimalPivot(tableaux.Last(), isMinLocal);

                        if (thetaCol == null && tableau == null)
                            break;

                        try
                        {
                            thetaColumns.Add(thetaCol.ToList());
                        }
                        catch (Exception)
                        {
                            break;
                        }
                        tableaux.Add(newTableau);
                        iterationPhases.Add(1);
                    }

                    var rhsValues = tableaux.Last().Select(row => row.Last()).ToList();
                    bool allRhsNonNegative = rhsValues.All(num => num >= 0);

                    if (!allRhsNonNegative)
                    {
                        tableaux.RemoveAt(tableaux.Count - 1);
                        pivotColumns.RemoveAt(pivotColumns.Count - 1);
                        pivotRows.RemoveAt(pivotRows.Count - 1);
                    }
                }

                var variableLabels = Enumerable.Range(1, objectiveLength).Select(i => $"y{i}").ToList();
                int varCount = 0, slackVarCount = 0, surplusVarCount = 0;
                var header = new List<string>();
                int headerSize = objectiveLength + surplusCount + slackCount;

                for (int i = 0; i < objectiveLength; i++)
                {
                    if (varCount < objectiveLength)
                    {
                        header.Add(variableLabels[varCount++]);
                    }
                }

                for (int i = 0; i < surplusCount; i++)
                {
                    if (slackVarCount < surplusCount)
                    {
                        header.Add($"u{surplusVarCount + 1}");
                        surplusVarCount++;
                    }
                }

                for (int i = 0; i < slackCount; i++)
                {
                    if (surplusVarCount < slackCount)
                    {
                        header.Add($"t{slackVarCount + 1}");
                        slackVarCount++;
                    }
                }

                header.Add("Solution");


                var variableColumns = new List<List<double>>();
                for (int k = 0; k < objectiveLength; k++)
                {
                    var column = tableaux.Last().Select(row => row[k]).ToList();
                    variableColumns.Add(column.Count(num => num != 0) == 1 ? column : null);
                }

                var decisionVariables = new List<double>();
                try
                {
                    decisionVariables = new List<double>();
                    for (int i = 0; i < variableColumns.Count; i++)
                    {
                        if (variableColumns[i] != null)
                        {
                            variableColumns[i] = variableColumns[i].Select(Math.Abs).ToList();
                            decisionVariables.Add(tableaux.Last()[variableColumns[i].IndexOf(1.0)].Last());
                        }
                        else
                        {
                            decisionVariables.Add(0);
                        }
                    }
                }
                catch
                {
                    decisionVariables = new List<double>();
                }

                double optimalValue = tableaux.Last()[0].Last();
                return (tableaux, decisionVariables, optimalValue, pivotColumns, pivotRows, headerRow);
            }
        }


        public class BranchAndBound
        {
            private int decimalPlaces = 4;
            private double epsilon = 1e-6;

            private DualSimplexSolver simplexSolver;

            private List<double> objectiveCoefficients = new List<double> { 0.0, 0.0 };
            private List<List<double>> constraintMatrix = new List<List<double>> { new List<double> { 0.0, 0.0, 0.0, 0.0 } };
            private List<List<List<double>>> simplexTableaux = new List<List<List<double>>>();

            private bool isMinimization = false;

            private List<double> optimalSolution = null;
            private double optimalValue;
            private int branchCount = 0;
            private List<(List<double> solution, double value)> feasibleSolutions = new List<(List<double>, double)>();
            private bool usePruning = false;

            private string optimalbranchLabel = null;
            private List<List<double>> optimalTableau = null;

            private List<object> outputTableaux = new List<object>();

            public BranchAndBound()
            {
                simplexSolver = new DualSimplexSolver();
                objectiveCoefficients = new List<double> { 0.0, 0.0 };
                constraintMatrix = new List<List<double>> { new List<double> { 0.0, 0.0, 0.0, 0.0 } };
                simplexTableaux = new List<List<List<double>>>();

                isMinimization = false;

                optimalSolution = null;
                optimalValue = isMinimization ? double.PositiveInfinity : double.NegativeInfinity;
                branchCount = 0;
                feasibleSolutions = new List<(List<double>, double)>();
                usePruning = false;

                optimalbranchLabel = null;
                optimalTableau = null;

                outputTableaux = new List<object>();
            }

            public double RoundNumber(double number)
            {
                try
                {
                    return Math.Round(number, decimalPlaces);
                }
                catch
                {
                    return number;
                }
            }

            public List<List<double>> RoundTableau(List<List<double>> tableau)
            {
                if (tableau == null) return tableau;

                var result = new List<List<double>>();
                foreach (var row in tableau)
                {
                    var roundedRow = new List<double>();
                    foreach (var val in row)
                    {
                        roundedRow.Add(RoundNumber(val));
                    }
                    result.Add(roundedRow);
                }
                return result;
            }

            public List<double> RoundVector(List<double> vector)
            {
                if (vector == null) return vector;

                var result = new List<double>();
                foreach (var val in vector)
                {
                    result.Add(RoundNumber(val));
                }
                return result;
            }

            public List<List<List<double>>> RoundAllTableaux(List<List<List<double>>> tableaux)
            {
                if (tableaux == null || tableaux.Count == 0)
                    return tableaux;

                var roundedTableaux = new List<List<List<double>>>();
                foreach (var tableau in tableaux)
                {
                    var roundedTableau = RoundTableau(tableau);
                    roundedTableaux.Add(roundedTableau);
                }
                return roundedTableaux;
            }

            public bool IsInteger(double value)
            {
                double roundedVal = RoundNumber(value);
                return Math.Abs(roundedVal - Math.Round(roundedVal)) <= epsilon;
            }

            public void DisplayTableau(List<List<double>> tableau, string caption = "Tableau")
            {
                var headers = new List<string>();
                for (int i = 0; i < objectiveCoefficients.Count; i++)
                {
                    headers.Add($"y{i + 1}");
                }
                for (int i = 0; i < (tableau[tableau.Count - 1].Count - objectiveCoefficients.Count - 1); i++)
                {
                    headers.Add($"t{i + 1}");
                }
                headers.Add("solution");

                Console.WriteLine($"\n{caption}");
                foreach (var header in headers)
                {
                    Console.Write($"{header,8}  ");
                }
                Console.WriteLine();

                foreach (var row in tableau)
                {
                    foreach (var val in row)
                    {
                        Console.Write($"{RoundNumber(val),8:F4}  ");
                    }
                    Console.WriteLine();
                }
                Console.WriteLine();
            }

            public List<int> IdentifyBasicVariables(List<List<List<double>>> tableaux)
            {
                var basicVars = new List<int>();

                for (int k = 0; k < tableaux[tableaux.Count - 1][tableaux[tableaux.Count - 1].Count - 1].Count; k++)
                {
                    int columnIndex = k;
                    var columnValues = new List<double>();

                    for (int i = 0; i < tableaux[tableaux.Count - 1].Count; i++)
                    {
                        double columnValue = RoundNumber(tableaux[tableaux.Count - 1][i][columnIndex]);
                        columnValues.Add(columnValue);
                    }

                    double sumValues = RoundNumber(columnValues.Sum());
                    if (Math.Abs(sumValues - 1.0) <= epsilon)
                    {
                        basicVars.Add(k);
                    }
                }

                var basicVarColumns = new List<List<double>>();
                for (int i = 0; i < tableaux[tableaux.Count - 1][tableaux[tableaux.Count - 1].Count - 1].Count; i++)
                {
                    var column = new List<double>();
                    if (basicVars.Contains(i))
                    {
                        for (int j = 0; j < tableaux[tableaux.Count - 1].Count; j++)
                        {
                            double roundedVal = RoundNumber(tableaux[tableaux.Count - 1][j][i]);
                            column.Add(roundedVal);
                        }
                        basicVarColumns.Add(column);
                    }
                }

                var zippedColumns = basicVarColumns.Zip(basicVars, (col, spot) => new { Col = col, Spot = spot }).ToList();
                var sortedColumns = zippedColumns.OrderBy(x => x.Col.Contains(1.0) ? x.Col.IndexOf(1.0) : x.Col.Count).ToList();

                if (sortedColumns.Any())
                {
                    basicVars = sortedColumns.Select(x => x.Spot).ToList();
                }
                else
                {
                    basicVars = new List<int>();
                }

                return basicVars;
            }

            public (List<List<double>> outputTab, List<List<double>> updatedTab) AddConstraint(List<List<double>> newConstraints, List<List<double>> baseTableau = null)
            {
                List<List<double>> workingTableau;
                List<int> basicVariables;

                if (baseTableau != null)
                {
                    workingTableau = DeepCopy(baseTableau);
                    workingTableau = RoundTableau(workingTableau);
                    var tempTableaux = new List<List<List<double>>> { workingTableau };
                    basicVariables = IdentifyBasicVariables(tempTableaux);
                }
                else
                {
                    Console.WriteLine("Input tableau required");
                    return (null, null);
                }

                var updatedTableau = DeepCopy(workingTableau);

                for (int k = 0; k < newConstraints.Count; k++)
                {
                    for (int i = 0; i < workingTableau.Count; i++)
                    {
                        updatedTableau[i].Insert(updatedTableau[i].Count - 1, 0.0);
                    }

                    var newConstraint = new List<double>();
                    for (int i = 0; i < workingTableau[0].Count + newConstraints.Count; i++)
                    {
                        newConstraint.Add(0.0);
                    }

                    for (int i = 0; i < newConstraints[k].Count - 2; i++)
                    {
                        newConstraint[i] = RoundNumber(newConstraints[k][i]);
                    }

                    newConstraint[newConstraint.Count - 1] = RoundNumber(newConstraints[k][newConstraints[k].Count - 2]);

                    int slackPosition = ((newConstraint.Count - newConstraints.Count) - 1) + k;
                    if (newConstraints[k][newConstraints[k].Count - 1] == 1)
                    {
                        newConstraint[slackPosition] = -1.0;
                    }
                    else
                    {
                        newConstraint[slackPosition] = 1.0;
                    }

                    updatedTableau.Add(newConstraint);
                }

                updatedTableau = RoundTableau(updatedTableau);
                DisplayTableau(updatedTableau, "Initial tableau with constraint");

                var outputTableau = DeepCopy(updatedTableau);

                for (int k = 0; k < newConstraints.Count; k++)
                {
                    int constraintRowIndex = updatedTableau.Count - newConstraints.Count + k;

                    foreach (int colIndex in basicVariables)
                    {
                        double coefficient = RoundNumber(outputTableau[constraintRowIndex][colIndex]);

                        if (Math.Abs(coefficient) > epsilon)
                        {
                            int? pivotRow = null;
                            for (int rowIndex = 0; rowIndex < outputTableau.Count - newConstraints.Count; rowIndex++)
                            {
                                if (Math.Abs(RoundNumber(outputTableau[rowIndex][colIndex]) - 1.0) <= epsilon)
                                {
                                    pivotRow = rowIndex;
                                    break;
                                }
                            }

                            if (pivotRow.HasValue)
                            {
                                int constraintType = (int)newConstraints[k][newConstraints[k].Count - 1];
                                bool reverseOperation = (constraintType == 1);

                                for (int col = 0; col < outputTableau[0].Count; col++)
                                {
                                    double pivotVal = RoundNumber(outputTableau[pivotRow.Value][col]);
                                    double constraintVal = RoundNumber(outputTableau[constraintRowIndex][col]);

                                    double newVal;
                                    if (reverseOperation)
                                    {
                                        newVal = pivotVal - coefficient * constraintVal;
                                    }
                                    else
                                    {
                                        newVal = constraintVal - coefficient * pivotVal;
                                    }

                                    outputTableau[constraintRowIndex][col] = RoundNumber(newVal);
                                }
                            }
                        }
                    }
                }

                outputTableau = RoundTableau(outputTableau);
                DisplayTableau(outputTableau, "Adjusted tableau");

                return (outputTableau, updatedTableau);
            }

            public (int? varIndex, double? value) CheckIntegerBasicVar(List<List<List<double>>> tableaux)
            {
                var decisionVars = new List<double>();

                for (int i = 0; i < objectiveCoefficients.Count; i++)
                {
                    bool found = false;
                    for (int j = 0; j < tableaux[tableaux.Count - 1].Count; j++)
                    {
                        double val = RoundNumber(tableaux[tableaux.Count - 1][j][i]);
                        if (Math.Abs(val - 1.0) <= epsilon)
                        {
                            double rhsVal = RoundNumber(tableaux[tableaux.Count - 1][j][tableaux[tableaux.Count - 1][j].Count - 1]);
                            decisionVars.Add(rhsVal);
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        decisionVars.Add(0.0);
                    }
                }

                int bestVarIndex = -1;
                double? bestValue = null;
                double minDistanceToHalf = double.PositiveInfinity;

                for (int i = 0; i < decisionVars.Count; i++)
                {
                    if (!IsInteger(decisionVars[i]))
                    {
                        double fractionalPart = decisionVars[i] - Math.Floor(decisionVars[i]);
                        double distanceToHalf = Math.Abs(fractionalPart - 0.5);

                        if (distanceToHalf < minDistanceToHalf)
                        {
                            minDistanceToHalf = distanceToHalf;
                            bestVarIndex = i;
                            bestValue = decisionVars[i];
                        }
                    }
                }

                if (bestVarIndex == -1)
                {
                    return (null, null);
                }
                else
                {
                    return (bestVarIndex, bestValue);
                }
            }

            public (List<double> lowerBound, List<double> upperBound) CreateBranches(List<List<List<double>>> tableaux)
            {
                var (varIndex, value) = CheckIntegerBasicVar(tableaux);

                if (varIndex == null && value == null)
                {
                    return (null, null);
                }

                Console.WriteLine($"Branching on y{varIndex + 1} = {RoundNumber(value.Value)}");

                int upperInt = (int)Math.Ceiling(value.Value);
                int lowerInt = (int)Math.Floor(value.Value);

                var lowerBound = new List<double>();
                for (int i = 0; i < objectiveCoefficients.Count; i++)
                {
                    lowerBound.Add(i == varIndex ? 1 : 0);
                }
                lowerBound.Add(lowerInt);
                lowerBound.Add(0);

                var upperBound = new List<double>();
                for (int i = 0; i < objectiveCoefficients.Count; i++)
                {
                    upperBound.Add(i == varIndex ? 1 : 0);
                }
                upperBound.Add(upperInt);
                upperBound.Add(1);

                return (lowerBound, upperBound);
            }

            public double? GetObjective(List<List<List<double>>> tableaux)
            {
                if (tableaux == null || tableaux.Count == 0)
                    return null;
                return RoundNumber(tableaux[tableaux.Count - 1][0][tableaux[tableaux.Count - 1][0].Count - 1]);
            }

            public List<double> ExtractSolution(List<List<List<double>>> tableaux)
            {
                var solution = new List<double>();
                for (int i = 0; i < objectiveCoefficients.Count; i++)
                {
                    solution.Add(0.0);
                }

                for (int i = 0; i < objectiveCoefficients.Count; i++)
                {
                    for (int j = 0; j < tableaux[tableaux.Count - 1].Count; j++)
                    {
                        double val = RoundNumber(tableaux[tableaux.Count - 1][j][i]);
                        if (Math.Abs(val - 1.0) <= epsilon)
                        {
                            solution[i] = RoundNumber(tableaux[tableaux.Count - 1][j][tableaux[tableaux.Count - 1][j].Count - 1]);
                            break;
                        }
                    }
                }

                return solution;
            }

            public bool IsIntegerSolution(List<double> solution)
            {
                foreach (double val in solution)
                {
                    if (!IsInteger(val))
                    {
                        return false;
                    }
                }
                return true;
            }

            public bool UpdateOptimalSolution(List<List<List<double>>> tableaux, string branchLabel = null)
            {
                var objVal = GetObjective(tableaux);
                var solution = ExtractSolution(tableaux);

                if (objVal == null)
                    return false;

                if (IsIntegerSolution(solution))
                {
                    feasibleSolutions.Add((new List<double>(solution), objVal.Value));

                    if (isMinimization)
                    {
                        if (objVal.Value < optimalValue)
                        {
                            optimalValue = objVal.Value;
                            optimalSolution = solution;
                            optimalTableau = tableaux[tableaux.Count - 1];
                            optimalbranchLabel = branchLabel;

                            Console.WriteLine($"New optimal integer solution found: [{string.Join(", ", solution)}] with value {objVal.Value}");
                            return true;
                        }
                        else
                        {
                            Console.WriteLine($"Integer solution found: [{string.Join(", ", solution)}] with value {objVal.Value} (not better than current optimal)");
                        }
                    }
                    else
                    {
                        if (objVal.Value > optimalValue)
                        {
                            optimalValue = objVal.Value;
                            optimalSolution = solution;
                            optimalTableau = tableaux[tableaux.Count - 1];
                            optimalbranchLabel = branchLabel;

                            Console.WriteLine($"New optimal integer solution found: [{string.Join(", ", solution)}] with value {objVal.Value}");
                            return true;
                        }
                        else
                        {
                            Console.WriteLine($"Integer solution found: [{string.Join(", ", solution)}] with value {objVal.Value} (not better than current optimal)");
                        }
                    }
                }
                return false;
            }

            public bool ShouldPrunebranch(List<List<List<double>>> tableaux)
            {
                if (!usePruning)
                    return false;

                var objVal = GetObjective(tableaux);

                if (objVal == null)
                    return true;

                if (optimalSolution != null)
                {
                    if (isMinimization)
                        return objVal.Value >= optimalValue;
                    else
                        return objVal.Value <= optimalValue;
                }

                return false;
            }

            public (List<double> optimalSolution, double optimalValue) ExecuteBranchAndBound(List<List<List<double>>> initialTableaux, bool enablePruning = false)
            {
                this.usePruning = enablePruning;

                Console.WriteLine("Initiating Branch and Bound Algorithm");
                if (enablePruning)
                    Console.WriteLine("Pruning: Enabled");
                else
                    Console.WriteLine("Pruning: Disabled");
                Console.WriteLine(new string('-', 50));

                initialTableaux = RoundAllTableaux(initialTableaux);

                optimalSolution = null;
                optimalValue = isMinimization ? double.PositiveInfinity : double.NegativeInfinity;
                branchCount = 0;
                feasibleSolutions = new List<(List<double>, double)>();

                var branchStack = new Stack<(List<List<List<double>>> tabs, int depth, string branchLabel, List<string> constraintsPath, string parentLabel)>();
                branchStack.Push((initialTableaux, 0, "0", new List<string>(), null));

                var childCounters = new Dictionary<string, int>();

                int iteration = 0;
                while (branchStack.Count > 0)
                {
                    iteration++;

                    if (iteration > 20)
                    {
                        Console.WriteLine("Potential infinite loop detected");
                        break;
                    }

                    var (currentTableaux, depth, branchLabel, constraintsPath, parentLabel) = branchStack.Pop();
                    branchCount++;

                    currentTableaux = RoundAllTableaux(currentTableaux);

                    Console.WriteLine($"\n--- Processing branch {branchLabel} (Depth {depth}) ---");
                    if (parentLabel != null)
                        Console.WriteLine($"Parent branch: {parentLabel}");
                    Console.WriteLine($"Constraint Path: [{string.Join(", ", constraintsPath)}]");

                    if (ShouldPrunebranch(currentTableaux))
                    {
                        Console.WriteLine($"branch {branchLabel} pruned");
                        continue;
                    }

                    UpdateOptimalSolution(currentTableaux, branchLabel);

                    var (lowerBound, upperBound) = CreateBranches(currentTableaux);

                    if (lowerBound == null && upperBound == null)
                    {
                        var solution = ExtractSolution(currentTableaux);
                        var objVal = GetObjective(currentTableaux);
                        Console.WriteLine($"branch {branchLabel}: Integer solution [{string.Join(", ", solution)}] with value {objVal}");
                        continue;
                    }

                    if (!childCounters.ContainsKey(branchLabel))
                        childCounters[branchLabel] = 0;

                    var childbranchs = new List<(List<List<List<double>>>, int, string, List<string>, string)>();

                    try
                    {
                        childCounters[branchLabel]++;
                        string childLabel = branchLabel == "0" ? "1" : $"{branchLabel}.{childCounters[branchLabel]}";

                        Console.Write($"\nLower Branch (branch {childLabel}): {string.Join(", ", lowerBound)} ");
                        for (int i = 0; i < lowerBound.Count - 2; i++)
                        {
                            if (lowerBound[i] == 0 || lowerBound[i] == 0.0)
                                continue;
                            if (lowerBound[i] == 1 || lowerBound[i] == 1.0)
                                Console.Write($"y{i + 1} ");
                            else
                                Console.Write($"{lowerBound[i]}*y{i + 1} ");
                        }
                        if (lowerBound[lowerBound.Count - 1] == 0)
                            Console.Write("<= ");
                        else
                            Console.Write(">= ");
                        Console.Write($"{lowerBound[lowerBound.Count - 2]} ");

                        var tempConsLower = new List<List<double>> { lowerBound };
                        var (outputTabLower, newTabLower) = AddConstraint(tempConsLower, currentTableaux[currentTableaux.Count - 1]);

                        var (newTableauxLower, changingVarsLower, optimalSolutionLower, pivotColsLower, pivotRowsLower, headerRowLower) =
                            simplexSolver.DoDualSimplex(new List<double>(), new List<List<double>>(), isMinimization, outputTabLower);

                        if (optimalSolutionLower == null)
                        {
                            for (int i = 0; i < headerRowLower.Count; i++)
                            {
                                Console.Write($"{headerRowLower[i],8:F4}  ");
                            }
                            DisplayTableau(newTableauxLower[0], $"branch {childLabel}: Infeasible tableau");
                            newTableauxLower = new List<List<List<double>>>();
                        }

                        if (optimalSolutionLower != null)
                        {
                            if (newTableauxLower != null && newTableauxLower.Count > 0)
                            {
                                newTableauxLower = RoundAllTableaux(newTableauxLower);
                                string constraintDesc = $"y{lowerBound.Take(lowerBound.Count - 2).ToList().IndexOf(1) + 1} <= {lowerBound[lowerBound.Count - 2]}";
                                var newConstraintsPath = new List<string>(constraintsPath) { constraintDesc };
                                childbranchs.Add((newTableauxLower, depth + 1, childLabel, newConstraintsPath, branchLabel));
                            }

                            Console.WriteLine($"Lower branch (branch {childLabel}) infeasible");
                        }

                        if (newTableauxLower != null && newTableauxLower.Count > 0)
                        {
                            for (int i = 0; i < newTableauxLower.Count - 1; i++)
                            {
                                DisplayTableau(newTableauxLower[i], $"branch {childLabel} Lower branch Tableau {i + 1}");
                                outputTableaux.Add(newTableauxLower[i]);
                            }
                            DisplayTableau(newTableauxLower[newTableauxLower.Count - 1], $"branch {childLabel} Lower branch final tableau");
                            outputTableaux.Add(newTableauxLower[newTableauxLower.Count - 1]);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Lower branch (branch {childCounters[branchLabel]}) failed: {e}");
                    }

                    try
                    {
                        childCounters[branchLabel]++;
                        string childLabel = branchLabel == "0" ? "2" : $"{branchLabel}.{childCounters[branchLabel]}";

                        Console.Write($"\nUpper Branch (branch {childLabel}): {string.Join(", ", upperBound)} ");
                        for (int i = 0; i < upperBound.Count - 2; i++)
                        {
                            if (upperBound[i] == 0 || upperBound[i] == 0.0)
                                continue;
                            if (upperBound[i] == 1 || upperBound[i] == 1.0)
                                Console.Write($"y{i + 1} ");
                            else
                                Console.Write($"{upperBound[i]}*y{i + 1} ");
                        }
                        if (upperBound[upperBound.Count - 1] == 0)
                            Console.Write("<= ");
                        else
                            Console.Write(">= ");
                        Console.Write($"{upperBound[upperBound.Count - 2]} ");

                        var tempConsUpper = new List<List<double>> { upperBound };
                        var (outputTabUpper, newTabUpper) = AddConstraint(tempConsUpper, currentTableaux[currentTableaux.Count - 1]);

                        var (newTableauxUpper, changingVarsUpper, optimalSolutionUpper, pivotColsUpper, pivotRowsUpper, headerRowUpper) =
                            simplexSolver.DoDualSimplex(new List<double>(), new List<List<double>>(), isMinimization, outputTabUpper);

                        if (optimalSolutionUpper == null)
                        {
                            DisplayTableau(newTableauxUpper[0], $"branch {childLabel}: Infeasible tableau");
                            newTableauxUpper = new List<List<List<double>>>();
                        }

                        if (optimalSolutionUpper != null)
                        {
                            if (newTableauxUpper != null && newTableauxUpper.Count > 0)
                            {
                                newTableauxUpper = RoundAllTableaux(newTableauxUpper);
                                string constraintDesc = $"y{upperBound.Take(upperBound.Count - 2).ToList().IndexOf(1) + 1} >= {upperBound[upperBound.Count - 2]}";
                                var newConstraintsPath = new List<string>(constraintsPath) { constraintDesc };
                                childbranchs.Add((newTableauxUpper, depth + 1, childLabel, newConstraintsPath, branchLabel));
                            }

                            Console.WriteLine($"Upper branch (branch {childLabel}) infeasible");
                        }

                        if (newTableauxUpper != null && newTableauxUpper.Count > 0)
                        {
                            for (int i = 0; i < newTableauxUpper.Count - 1; i++)
                            {
                                DisplayTableau(newTableauxUpper[i], $"branch {childLabel} Upper branch Tableau {i + 1}");
                            }
                            DisplayTableau(newTableauxUpper[newTableauxUpper.Count - 1], $"branch {childLabel} Upper branch final tableau");
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Upper branch (branch {childCounters[branchLabel]}) failed: {e}");
                    }

                    for (int i = childbranchs.Count - 1; i >= 0; i--)
                    {
                        branchStack.Push(childbranchs[i]);
                    }
                }

                Console.WriteLine("\n" + new string('-', 50));
                Console.WriteLine("BRANCH AND BOUND COMPLETED");
                Console.WriteLine(new string('-', 50));
                if (optimalSolution != null)
                {
                    DisplayTableau(optimalTableau, $"Optimal solution tableau at branch {optimalbranchLabel}");
                    Console.WriteLine($"Optimal branch: {optimalbranchLabel}");
                    Console.WriteLine($"Optimal integer solution: [{string.Join(", ", optimalSolution)}]");
                    Console.WriteLine($"Optimal value: {optimalValue}");
                }
                else
                {
                    Console.WriteLine("No integer solution found");
                }
                Console.WriteLine($"Total branchs processed: {branchCount}");

                return (optimalSolution, optimalValue);
            }

            public (List<double> objective, List<List<double>> constraints) ConfigureProblem(
                List<double> objective,
                List<List<double>> constraints)
            {
                int numConstraints = objective.Count;
                int vectorLength = objective.Count + 3;

                for (int i = 0; i < numConstraints; i++)
                {
                    var row = new List<double>(new double[vectorLength]);
                    row[i] = 1;
                    row[vectorLength - 2] = 1;
                    constraints.Add(row);
                }

                return (objective, constraints);
            }

            public void RunBranchAndBound(List<double> objectivePassed, List<List<double>> constraintsPassed, bool isMin)
            {
                bool enablePruning = false;
                try
                {
                    objectiveCoefficients = objectivePassed.ToList();
                    constraintMatrix = constraintsPassed.Select(x => x.ToList()).ToList();
                    (objectiveCoefficients, constraintMatrix) = ConfigureProblem(objectiveCoefficients, constraintMatrix);

                    var objCopy = new List<double>(objectiveCoefficients);
                    var consCopy = new List<List<double>>();
                    foreach (var constraint in constraintMatrix)
                    {
                        consCopy.Add(new List<double>(constraint));
                    }

                    try
                    {
                        var (newTableaux, changingVars, optimalSolution, _, _, _) = simplexSolver.DoDualSimplex(objCopy, consCopy, isMin);

                        this.simplexTableaux = RoundAllTableaux(newTableaux);

                        Console.WriteLine("Initial LP relaxation solved");
                        for (int i = 0; i < this.simplexTableaux.Count - 1; i++)
                        {
                            DisplayTableau(this.simplexTableaux[i], $"Initial Tableau {i + 1}");
                        }
                        DisplayTableau(this.simplexTableaux[this.simplexTableaux.Count - 1], "Initial tableau solved");
                        var solution = ExtractSolution(this.simplexTableaux);
                        var objVal = GetObjective(this.simplexTableaux);
                        Console.WriteLine($"Initial solution: [{string.Join(", ", solution)}]");
                        Console.WriteLine($"Initial objective value: {objVal}");

                        var (bestSolution, bestValue) = ExecuteBranchAndBound(this.simplexTableaux, enablePruning);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Simplex error: {e}");
                        throw;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Calculation error: {e}");
                    throw;
                }
            }

            private List<List<double>> DeepCopy(List<List<double>> original)
            {
                if (original == null) return null;

                var copy = new List<List<double>>();
                foreach (var row in original)
                {
                    copy.Add(new List<double>(row));
                }
                return copy;
            }
        }
    }
}
