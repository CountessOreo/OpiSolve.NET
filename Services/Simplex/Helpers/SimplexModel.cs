using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OptiSolver.NET.Services.Simplex.Helpers
{
    public class SimplexModel
    {
        private string problemType;
        private int[] objFunction;
        private int[,] constraintsCoefficients;
        private string[] operatorsConstraints;
        private int[] rhsConstraints;
        private string[] signRestrictions;

        public string ProblemType { get => problemType; set => problemType = value; }
        public int[] ObjFunction { get => objFunction; set => objFunction = value; }
        public int[,] ConstraintsCoefficients { get => constraintsCoefficients; set => constraintsCoefficients = value; }
        public string[] OperatorsConstraints { get => operatorsConstraints; set => operatorsConstraints = value; }
        public int[] RhsConstraints { get => rhsConstraints; set => rhsConstraints = value; }
        public string[] SignRestrictions { get => signRestrictions; set => signRestrictions = value; }

        public SimplexModel() { }

        public SimplexModel(string problemType, int[] objFunction, int[,] constraintsCoefficients, string[] operatorsConstraints, int[] rhsConstraints, string[] signRestrictions)
        {
            this.problemType = problemType;
            this.objFunction = objFunction;
            this.constraintsCoefficients = constraintsCoefficients;
            this.operatorsConstraints = operatorsConstraints;
            this.rhsConstraints = rhsConstraints;
            this.signRestrictions = signRestrictions;
        }

        public override string ToString()
        {
            string constraintsStr = "";
            for (int i = 0; i < ConstraintsCoefficients.GetLength(0); i++)
            {
                for (int j = 0; j < ConstraintsCoefficients.GetLength(1); j++)
                {
                    constraintsStr += ConstraintsCoefficients[i, j] + " ";
                }
                constraintsStr += OperatorsConstraints[i] + RhsConstraints[i] + "\n";
            }

            return $"IP Model Values:\n" +
                   $"----------------\n" +
                   $"Problem Type: {ProblemType}\n \n" +
                   $"Objective Function: {string.Join(" ", ObjFunction)}\n \n" +
                   $"Constraints:\n{constraintsStr}\n" +
                   $"Sign Restrictions: {string.Join(" ", SignRestrictions)}\n";
        }
    }
}
