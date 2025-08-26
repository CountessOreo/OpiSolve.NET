using OptiSolver.NET.Core;
using System;
using System.Collections.Generic;

namespace OptiSolver.NET.Services.Simplex
{
    /// <summary>
    /// Standard Primal Simplex Algorithm for LPModel.
    /// Uses canonical form and tableau operations.
    /// </summary>
    public class SimplexTableau
    {
        private readonly LPModel model;
        private readonly CanonicalForm canonical;
        private double[,] tableau;
        private int iteration;

        public SimplexTableau(LPModel model)
        {
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            this.canonical = new ModelCanonicalTransformer().Canonicalize(model);
        }

        /// <summary>
        /// Solves the LP using the simplex tableau method.
        /// </summary>
        public void Solve()
        {
            PrepareTableau();

            bool optimal = false;
            iteration = 0;

            while (!optimal)
            {
                iteration++;

                int entering = FindEnteringVariable();
                if (IsOptimal(entering))
                {
                    optimal = true;
                    break;
                }

                int exiting = FindExitingVariable(entering);
                if (exiting == -1)
                    throw new InvalidOperationException("Unbounded solution.");

                Pivot(exiting, entering);
            }

            // Optionally: update model variable values from tableau here
        }

        private void PrepareTableau()
        {
            int m = canonical.ConstraintCount;
            int n = canonical.TotalVariables;
            tableau = new double[m + 1, n + 1];

            for (int i = 0; i < m; i++)
            {
                for (int j = 0; j < n; j++)
                    tableau[i + 1, j] = canonical.ConstraintMatrix[i, j];
                tableau[i + 1, n] = canonical.RightHandSide[i];
            }

            double sign = canonical.ObjectiveType == ObjectiveType.Maximize ? -1.0 : 1.0;
            for (int j = 0; j < n; j++)
                tableau[0, j] = sign * canonical.ObjectiveCoefficients[j];
            tableau[0, n] = 0.0;
        }

        private int FindEnteringVariable()
        {
            int n = tableau.GetLength(1) - 1;
            int idx = 0;
            double best = tableau[0, 0];

            for (int j = 1; j < n; j++)
            {
                if (canonical.ObjectiveType == ObjectiveType.Maximize)
                {
                    if (tableau[0, j] < best)
                    {
                        best = tableau[0, j];
                        idx = j;
                    }
                }
                else
                {
                    if (tableau[0, j] > best)
                    {
                        best = tableau[0, j];
                        idx = j;
                    }
                }
            }
            return idx;
        }

        private bool IsOptimal(int entering)
        {
            if (canonical.ObjectiveType == ObjectiveType.Maximize)
                return tableau[0, entering] >= 0;
            else
                return tableau[0, entering] <= 0;
        }

        private int FindExitingVariable(int entering)
        {
            int m = tableau.GetLength(0);
            int n = tableau.GetLength(1);
            int exiting = -1;
            double minRatio = double.MaxValue;

            for (int i = 1; i < m; i++)
            {
                double coeff = tableau[i, entering];
                if (coeff > 1e-9)
                {
                    double ratio = tableau[i, n - 1] / coeff;
                    if (ratio < minRatio)
                    {
                        minRatio = ratio;
                        exiting = i;
                    }
                }
            }
            return exiting;
        }

        private void Pivot(int exitingRow, int enteringCol)
        {
            int n = tableau.GetLength(1);
            double pivot = tableau[exitingRow, enteringCol];
            for (int j = 0; j < n; j++)
                tableau[exitingRow, j] /= pivot;

            for (int i = 0; i < tableau.GetLength(0); i++)
            {
                if (i == exitingRow) continue;
                double factor = tableau[i, enteringCol];
                for (int j = 0; j < n; j++)
                    tableau[i, j] -= factor * tableau[exitingRow, j];
            }
        }

        public override string ToString()
        {
            int m = tableau.GetLength(0);
            int n = tableau.GetLength(1);

            var headers = new List<string>();
            for (int j = 0; j < canonical.TotalVariables; j++)
                headers.Add($"x{j + 1}");
            headers.Add("RHS");

            string headerRow = string.Join(" | ", headers);
            string separator = new string('-', headerRow.Length);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(headerRow);
            sb.AppendLine(separator);

            for (int i = 0; i < m; i++)
            {
                for (int j = 0; j < n; j++)
                    sb.Append(tableau[i, j].ToString("F3").PadLeft(10));
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
