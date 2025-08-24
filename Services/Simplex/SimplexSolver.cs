using OptiSolver.NET.Services.Simplex.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OptiSolver.NET.Services.Simplex
{
    public class SimplexSolver
    {
        public static double[] Solve(SimplexModel model)
        {
            // Extract data from the Model object
            double[] objective = Array.ConvertAll(model.ObjFunction, item => (double)item);
            double[,] constraints = ConvertConstraintsToDouble(model.ConstraintsCoefficients);
            double[] bounds = Array.ConvertAll(model.RhsConstraints, item => (double)item);

            int numVariables = objective.Length;
            int numConstraints = bounds.Length;

            // Initializing the tableau
            double[,] tableau = InitializeTableau(objective, constraints, bounds, numVariables, numConstraints);

            // Perform the Simplex algorithm
            while (true)
            {
                // Identify the pivot column (most negative coefficient in the objective row)
                int pivotColumn = -1;
                double mostNegative = 0;
                for (int j = 0; j < numVariables; j++)
                {
                    if (tableau[numConstraints, j] < mostNegative)
                    {
                        mostNegative = tableau[numConstraints, j];
                        pivotColumn = j;
                    }
                }

                // If there's no negative coefficient, the optimal solution has been found
                if (pivotColumn == -1)
                {
                    break;
                }

                // Identify the pivot row (minimum ratio test)
                int pivotRow = -1;
                double minRatio = double.PositiveInfinity;
                for (int i = 0; i < numConstraints; i++)
                {
                    if (tableau[i, pivotColumn] > 0)
                    {
                        double ratio = tableau[i, numVariables + numConstraints] / tableau[i, pivotColumn];
                        if (ratio < minRatio)
                        {
                            minRatio = ratio;
                            pivotRow = i;
                        }
                    }
                }

                // If no valid pivot row is found, the problem is unbounded
                if (pivotRow == -1)
                {
                    throw new Exception("The problem is unbounded.");
                }

                // Pivot operation
                Pivot(tableau, pivotColumn, pivotRow, numVariables, numConstraints);
            }

            // Extract the solution
            double[] solution = new double[numVariables];
            for (int j = 0; j < numVariables; j++)
            {
                solution[j] = 0;  // Initialize with 0
                for (int i = 0; i < numConstraints; i++)
                {
                    if (tableau[i, j] == 1 && tableau[i, numVariables + i] == 1)
                    {
                        solution[j] = tableau[i, numVariables + numConstraints];
                        break;
                    }
                }
            }

            return solution;
        }

        private static double[,] InitializeTableau(double[] objective, double[,] constraints, double[] bounds, int numVariables, int numConstraints)
        {
            double[,] tableau = new double[numConstraints + 1, numVariables + numConstraints + 1];

            // Fill the tableau with constraints coefficients and bounds
            for (int i = 0; i < numConstraints; i++)
            {
                for (int j = 0; j < numVariables; j++)
                {
                    tableau[i, j] = constraints[i, j];
                }
                tableau[i, numVariables + i] = 1; // Add slack variables
                tableau[i, numVariables + numConstraints] = bounds[i];
            }

            // Fill the objective function row
            for (int j = 0; j < numVariables; j++)
            {
                tableau[numConstraints, j] = -objective[j];
            }

            return tableau;
        }

        private static void Pivot(double[,] tableau, int pivotColumn, int pivotRow, int numVariables, int numConstraints)
        {
            double pivotElement = tableau[pivotRow, pivotColumn];

            // Normalize the pivot row
            for (int j = 0; j <= numVariables + numConstraints; j++)
            {
                tableau[pivotRow, j] /= pivotElement;
            }

            // Eliminate the pivot column from other rows
            for (int i = 0; i <= numConstraints; i++)
            {
                if (i != pivotRow)
                {
                    double factor = tableau[i, pivotColumn];
                    for (int j = 0; j <= numVariables + numConstraints; j++)
                    {
                        tableau[i, j] -= factor * tableau[pivotRow, j];
                    }
                }
            }
        }

        private static double[,] ConvertConstraintsToDouble(int[,] intArray)
        {
            int rows = intArray.GetLength(0);
            int cols = intArray.GetLength(1);
            double[,] doubleArray = new double[rows, cols];

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    doubleArray[i, j] = (double)intArray[i, j];
                }
            }

            return doubleArray;
        }
    }
}
