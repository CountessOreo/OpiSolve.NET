using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OptiSolver.NET.Core
{
    /// <summary>
    /// Represents a single constraint in the optimization model
    /// </summary>
    public class Constraint
    {
        // Index of this constraint (0-based)
        public int Index { get; set; }

        // Display name for this constraint (C1, C2, C3, ...)
        public string Name { get; set; }

        /// <summary>
        /// Coefficients for each decision variable on the left-hand side
        /// Index corresponds to variable index
        /// </summary>
        public List<double> Coefficients { get; set; }

        // The relationship type (<=, =, >=)
        public ConstraintRelation Relation { get; set; }

        // Right-hand side value of the constraint
        public double RightHandSide { get; set; }

        // Slack/surplus variable value for this constraint (used during solving)
        public double SlackValue { get; set; }

        // Shadow price (dual value) for this constraint
        public double ShadowPrice { get; set; }

        // Whether this constraint is active (binding) at the optimal solution
        public bool IsActive { get; set; }

        /// <summary>
        /// Default constructor
        /// </summary>
        public Constraint()
        {
            Index = 0;
            Name = "C1";
            Coefficients = new List<double>();
            Relation = ConstraintRelation.LessThanOrEqual;
            RightHandSide = 0.0;
            SlackValue = 0.0;
            ShadowPrice = 0.0;
            IsActive = false;
        }

        /// <summary>
        /// Constructor with basic parameters
        /// </summary>
        /// <param name="index">Constraint index</param>
        /// <param name="coefficients">LHS coefficients</param>
        /// <param name="relation">Constraint relation</param>
        /// <param name="rhs">Right-hand side value</param>
        public Constraint(int index, List<double> coefficients, ConstraintRelation relation, double rhs)
        {
            Index = index;
            Name = $"C{index + 1}";
            Coefficients = new List<double>(coefficients ?? throw new ArgumentNullException(nameof(coefficients)));
            Relation = relation;
            RightHandSide = rhs;
            SlackValue = 0.0;
            ShadowPrice = 0.0;
            IsActive = false;
        }

        /// <summary>
        /// Constructor with double array for coefficients
        /// </summary>
        public Constraint(int index, double[] coefficients, ConstraintRelation relation, double rhs)
            : this(index, coefficients?.ToList(), relation, rhs)
        {
        }

        /// <summary>
        /// Full constructor with name
        /// </summary>
        public Constraint(int index, string name, List<double> coefficients, ConstraintRelation relation, double rhs)
            : this(index, coefficients, relation, rhs)
        {
            Name = name ?? $"C{index + 1}";
        }

        /// <summary>
        /// Gets the coefficient for a specific variable
        /// </summary>
        /// <param name="variableIndex">Index of the variable</param>
        /// <returns>Coefficient value (0 if index out of bounds)</returns>
        public double GetCoefficient(int variableIndex)
        {
            if (variableIndex < 0 || variableIndex >= Coefficients.Count)
                return 0.0;

            return Coefficients[variableIndex];
        }

        /// <summary>
        /// Sets the coefficient for a specific variable
        /// </summary>
        /// <param name="variableIndex">Index of the variable</param>
        /// <param name="coefficient">New coefficient value</param>
        public void SetCoefficient(int variableIndex, double coefficient)
        {
            // Extend coefficients list if necessary
            while (Coefficients.Count <= variableIndex)
            {
                Coefficients.Add(0.0);
            }

            Coefficients[variableIndex] = coefficient;
        }

        /// <summary>
        /// Evaluates the left-hand side of the constraint given variable values
        /// </summary>
        /// <param name="variableValues">Array of variable values</param>
        /// <returns>LHS value</returns>
        public double EvaluateLHS(double[] variableValues)
        {
            if (variableValues == null)
                throw new ArgumentNullException(nameof(variableValues));

            double lhsValue = 0.0;
            int minLength = Math.Min(Coefficients.Count, variableValues.Length);

            for (int i = 0; i < minLength; i++)
            {
                lhsValue += Coefficients[i] * variableValues[i];
            }

            return lhsValue;
        }

        /// <summary>
        /// Evaluates the left-hand side using Variable objects
        /// </summary>
        /// <param name="variables">List of variables with their current values</param>
        /// <returns>LHS value</returns>
        public double EvaluateLHS(List<Variable> variables)
        {
            if (variables == null)
                throw new ArgumentNullException(nameof(variables));

            double lhsValue = 0.0;

            for (int i = 0; i < Math.Min(Coefficients.Count, variables.Count); i++)
            {
                lhsValue += Coefficients[i] * variables[i].Value;
            }

            return lhsValue;
        }

        /// <summary>
        /// Checks if the constraint is satisfied given variable values
        /// </summary>
        /// <param name="variableValues">Array of variable values</param>
        /// <param name="tolerance">Numerical tolerance for comparison</param>
        /// <returns>True if constraint is satisfied</returns>
        public bool IsSatisfied(double[] variableValues, double tolerance = 1e-9)
        {
            double lhsValue = EvaluateLHS(variableValues);

            return Relation switch
            {
                ConstraintRelation.LessThanOrEqual => lhsValue <= RightHandSide + tolerance,
                ConstraintRelation.Equal => Math.Abs(lhsValue - RightHandSide) <= tolerance,
                ConstraintRelation.GreaterThanOrEqual => lhsValue >= RightHandSide - tolerance,
                _ => throw new ArgumentException($"Unknown constraint relation: {Relation}")
            };
        }

        /// <summary>
        /// Checks if constraint is satisfied using Variable objects
        /// </summary>
        public bool IsSatisfied(List<Variable> variables, double tolerance = 1e-9)
        {
            double lhsValue = EvaluateLHS(variables);

            return Relation switch
            {
                ConstraintRelation.LessThanOrEqual => lhsValue <= RightHandSide + tolerance,
                ConstraintRelation.Equal => Math.Abs(lhsValue - RightHandSide) <= tolerance,
                ConstraintRelation.GreaterThanOrEqual => lhsValue >= RightHandSide - tolerance,
                _ => throw new ArgumentException($"Unknown constraint relation: {Relation}")
            };
        }

        /// <summary>
        /// Calculates the violation amount if constraint is not satisfied
        /// </summary>
        /// <param name="variableValues">Array of variable values</param>
        /// <returns>Violation amount (positive if violated, 0 if satisfied)</returns>
        public double GetViolation(double[] x, double tol = 1e-9)
        {
            double lhs = EvaluateLHS(x);
            return Relation switch
            {
                ConstraintRelation.LessThanOrEqual => Math.Max(0, (lhs - RightHandSide) - tol),
                ConstraintRelation.GreaterThanOrEqual => Math.Max(0, (RightHandSide - lhs) - tol),
                ConstraintRelation.Equal => Math.Max(0, Math.Abs(lhs - RightHandSide) - tol),
                _ => throw new ArgumentException($"Unknown constraint relation: {Relation}")
            };
        }


        /// <summary>
        /// Converts constraint to standard form (all <=)
        /// Returns tuple of (newCoefficients, newRelation, newRHS, multiplier)
        /// </summary>
        public (List<double> coefficients, ConstraintRelation relation, double rhs, int multiplier) ToStandardForm()
        {
            switch (Relation)
            {
                case ConstraintRelation.LessThanOrEqual:
                // Already <= ; keep as-is
                return (new List<double>(Coefficients), ConstraintRelation.LessThanOrEqual, RightHandSide, 1);

                case ConstraintRelation.Equal:
                // Keep equality; artificials handled later
                return (new List<double>(Coefficients), ConstraintRelation.Equal, RightHandSide, 1);

                case ConstraintRelation.GreaterThanOrEqual:
                // ax >= b  ==>  -ax <= -b
                var negatedCoeffs = Coefficients.Select(c => -c).ToList();
                return (negatedCoeffs, ConstraintRelation.LessThanOrEqual, -RightHandSide, -1);

                default:
                throw new ArgumentException($"Unknown constraint relation: {Relation}");
            }
        }

        /// <summary>
        /// Creates a copy of this constraint
        /// </summary>
        public Constraint Clone()
        {
            return new Constraint
            {
                Index = this.Index,
                Name = this.Name,
                Coefficients = new List<double>(this.Coefficients),
                Relation = this.Relation,
                RightHandSide = this.RightHandSide,
                SlackValue = this.SlackValue,
                ShadowPrice = this.ShadowPrice,
                IsActive = this.IsActive
            };
        }

        /// <summary>
        /// String representation
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"{Name}: ");

            // Build LHS string
            for (int i = 0; i < Coefficients.Count; i++)
            {
                if (i > 0 && Coefficients[i] >= 0)
                    sb.Append(" + ");
                else if (i > 0)
                    sb.Append(" ");

                sb.Append($"{Coefficients[i]:F3}x{i + 1}");
            }

            // Add relation and RHS
            sb.Append($" {EnumHelper.RelationToString(Relation)} {RightHandSide:F3}");

            return sb.ToString();
        }

        /// <summary>
        /// Formatted string for tableau display
        /// </summary>
        public string ToTableauString(List<Variable> variables = null)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < Coefficients.Count; i++)
            {
                if (i > 0 && Coefficients[i] >= 0)
                    sb.Append(" +");

                sb.Append($"{Coefficients[i]:F3}");

                if (variables != null && i < variables.Count)
                    sb.Append($"·{variables[i].Name}");
                else
                    sb.Append($"·x{i + 1}");
            }

            sb.Append($" {EnumHelper.RelationToString(Relation)} {RightHandSide:F3}");
            return sb.ToString();
        }

        /// <summary>
        /// Equality comparison based on index
        /// </summary>
        public override bool Equals(object obj)
        {
            if (obj is Constraint other)
            {
                return Index == other.Index;
            }
            return false;
        }

        /// <summary>
        /// Hash code based on index
        /// </summary>
        public override int GetHashCode()
        {
            return Index.GetHashCode();
        }
    }
}
