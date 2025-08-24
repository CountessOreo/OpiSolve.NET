using OptiSolver.NET.Core;
using System;
using System.Collections.Generic;

namespace OptiSolver.NET.Services.Simplex
{
    /// <summary>
    /// Revised Primal Simplex Algorithm for LPModel.
    /// Uses canonical form and matrix operations.
    /// </summary>
    public class RevisedSimplexSolver
    {
        private readonly LPModel model;
        private readonly CanonicalForm canonical;
        private int iteration;

        // Basis indices and matrix
        private List<int> basisIndices;
        private double[,] B;
        private double[,] BInv;

        public RevisedSimplexSolver(LPModel model)
        {
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            this.canonical = new ModelCanonicalTransformer().Canonicalize(model);
        }

        /// <summary>
        /// Solves the LP using the revised simplex method.
        /// </summary>
        public void Solve()
        {
            PrepareBasis();

            bool optimal = false;
            iteration = 0;

            while (!optimal)
            {
                iteration++;

                // Compute reduced costs
                var reducedCosts = ComputeReducedCosts();

                // Find entering variable
                int entering = FindEnteringVariable(reducedCosts);
                if (IsOptimal(reducedCosts, entering))
                {
                    optimal = true;
                    break;
                }

                // Compute direction vector
                var direction = GetDirectionVector(entering);

                // Ratio test for leaving variable
                int leaving = FindLeavingVariable(direction);
                if (leaving == -1)
                    throw new InvalidOperationException("Unbounded solution.");

                // Update basis
                basisIndices[leaving] = entering;
                UpdateBasisMatrix();
            }
        }

        private void PrepareBasis()
        {
            int m = canonical.ConstraintCount;
            int n = canonical.TotalVariables;

            basisIndices = new List<int>();
            for (int i = 0; i < m; i++)
            {
                // Start with slack/artificial variables as basis
                basisIndices.Add(n - m + i);
            }
            UpdateBasisMatrix();
        }

        private void UpdateBasisMatrix()
        {
            int m = canonical.ConstraintCount;
            B = new double[m, m];
            for (int i = 0; i < m; i++)
            {
                for (int j = 0; j < m; j++)
                    B[i, j] = canonical.ConstraintMatrix[i, basisIndices[j]];
            }
            BInv = MatrixInverse(B);
        }

        private double[] ComputeReducedCosts()
        {
            int n = canonical.TotalVariables;
            int m = canonical.ConstraintCount;

            var cb = new double[m];
            for (int i = 0; i < m; i++)
                cb[i] = canonical.ObjectiveCoefficients[basisIndices[i]];

            var y = MatrixMultiply(cb, BInv);

            var reducedCosts = new double[n];
            for (int j = 0; j < n; j++)
            {
                double cj = canonical.ObjectiveCoefficients[j];
                var col = new double[m];
                for (int i = 0; i < m; i++)
                    col[i] = canonical.ConstraintMatrix[i, j];
                reducedCosts[j] = cj - DotProduct(y, col);
            }
            return reducedCosts;
        }

        private int FindEnteringVariable(double[] reducedCosts)
        {
            int idx = 0;
            double best = reducedCosts[0];
            for (int j = 1; j < reducedCosts.Length; j++)
            {
                if (canonical.ObjectiveType == ObjectiveType.Maximize)
                {
                    if (reducedCosts[j] < best)
                    {
                        best = reducedCosts[j];
                        idx = j;
                    }
                }
                else
                {
                    if (reducedCosts[j] > best)
                    {
                        best = reducedCosts[j];
                        idx = j;
                    }
                }
            }
            return idx;
        }

        private bool IsOptimal(double[] reducedCosts, int entering)
        {
            if (canonical.ObjectiveType == ObjectiveType.Maximize)
                return reducedCosts[entering] >= 0;
            else
                return reducedCosts[entering] <= 0;
        }

        private double[] GetDirectionVector(int entering)
        {
            int m = canonical.ConstraintCount;
            var a = new double[m];
            for (int i = 0; i < m; i++)
                a[i] = canonical.ConstraintMatrix[i, entering];
            return MatrixMultiply(BInv, a);
        }

        private int FindLeavingVariable(double[] direction)
        {
            int m = canonical.ConstraintCount;
            var xB = MatrixMultiply(BInv, canonical.RightHandSide);

            int leaving = -1;
            double minRatio = double.MaxValue;
            for (int i = 0; i < m; i++)
            {
                if (direction[i] > 1e-9)
                {
                    double ratio = xB[i] / direction[i];
                    if (ratio < minRatio)
                    {
                        minRatio = ratio;
                        leaving = i;
                    }
                }
            }
            return leaving;
        }

        // --- Matrix helpers ---

        private static double[] MatrixMultiply(double[] row, double[,] mat)
        {
            int cols = mat.GetLength(1);
            var result = new double[cols];
            for (int j = 0; j < cols; j++)
                for (int i = 0; i < row.Length; i++)
                    result[j] += row[i] * mat[i, j];
            return result;
        }

        private static double[] MatrixMultiply(double[,] mat, double[] col)
        {
            int rows = mat.GetLength(0);
            int cols = mat.GetLength(1);
            var result = new double[rows];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    result[i] += mat[i, j] * col[j];
            return result;
        }

        private static double DotProduct(double[] a, double[] b)
        {
            double sum = 0.0;
            for (int i = 0; i < a.Length; i++)
                sum += a[i] * b[i];
            return sum;
        }

        private static double[,] MatrixInverse(double[,] matrix)
        {
            // Basic Gauss-Jordan for small matrices
            int n = matrix.GetLength(0);
            var result = new double[n, n];
            var augmented = new double[n, 2 * n];

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                    augmented[i, j] = matrix[i, j];
                augmented[i, n + i] = 1;
            }

            for (int i = 0; i < n; i++)
            {
                double diag = augmented[i, i];
                for (int j = 0; j < 2 * n; j++)
                    augmented[i, j] /= diag;

                for (int k = 0; k < n; k++)
                {
                    if (k == i) continue;
                    double factor = augmented[k, i];
                    for (int j = 0; j < 2 * n; j++)
                        augmented[k, j] -= factor * augmented[i, j];
                }
            }

            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    result[i, j] = augmented[i, n + j];

            return result;
        }
    }
}
