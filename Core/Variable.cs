namespace OptiSolver.NET.Core
{
    /// <summary>
    /// Represents a single decision variable in the optimization model
    /// </summary>
    public class Variable
    {
        /// Zero-based index of the variable (x0, x1, x2, ...)
        public int Index { get; set; }

        /// Display name of the variable (x1, x2, x3, ...)
        public string Name { get; set; }

        /// Coefficient of this variable in the objective function
        public double Coefficient { get; set; }

        /// Type and restrictions on this variable (continuous, integer, binary, etc.)
        public VariableType Type { get; set; }

        /// Current value of the variable in a solution (used during solving)
        public double Value { get; set; }

        /// Lower bound for the variable (default: 0 for non-negative variables)
        public double LowerBound { get; set; }

        /// Upper bound for the variable (default: positive infinity)
        public double UpperBound { get; set; }

        /// Whether this variable is basic in the current solution
        public bool IsBasic { get; set; }

        /// <summary>
        /// Default constructor
        /// </summary>
        public Variable()
        {
            Index = 0;
            Name = "x0";
            Coefficient = 0.0;
            Type = VariableType.Positive;
            Value = 0.0;
            LowerBound = 0.0;
            UpperBound = double.PositiveInfinity;
            IsBasic = false;
        }

        /// <summary>
        /// Constructor with basic parameters
        /// </summary>
        /// <param name="index">Zero-based index</param>
        /// <param name="coefficient">Objective function coefficient</param>
        /// <param name="type">Variable type and restrictions</param>
        public Variable(int index, double coefficient, VariableType type)
        {
            Index = index;
            Name = $"x{index + 1}";
            Coefficient = coefficient;
            Type = type;
            Value = 0.0;
            IsBasic = false;
            SetBoundsFromType(type);
        }

        /// <summary>
        /// Full constructor with all parameters
        /// </summary>
        public Variable(int index, string name, double coefficient, VariableType type,
                       double lowerBound = 0.0, double upperBound = double.PositiveInfinity)
        {
            Index = index;
            Name = name ?? $"x{index + 1}";
            Coefficient = coefficient;
            Type = type;
            Value = 0.0;
            LowerBound = lowerBound;
            UpperBound = upperBound;
            IsBasic = false;
            ValidateBounds();
        }

        /// <summary>
        /// Sets appropriate bounds based on variable type
        /// </summary>
        private void SetBoundsFromType(VariableType type)
        {
            switch (type)
            {
                case VariableType.Positive:
                LowerBound = 0.0;
                UpperBound = double.PositiveInfinity;
                break;

                case VariableType.Negative:
                LowerBound = double.NegativeInfinity;
                UpperBound = 0.0;
                break;

                case VariableType.Unrestricted:
                LowerBound = double.NegativeInfinity;
                UpperBound = double.PositiveInfinity;
                break;

                case VariableType.Binary:
                LowerBound = 0.0;
                UpperBound = 1.0;
                break;

                case VariableType.Integer:
                LowerBound = 0.0;
                UpperBound = double.PositiveInfinity;
                break;

                case VariableType.Continuous:
                LowerBound = 0.0;
                UpperBound = double.PositiveInfinity;
                break;

                default:
                throw new ArgumentException($"Unknown variable type: {type}");
            }
        }

        /// <summary>
        /// Validates that the bounds are consistent with the variable type
        /// </summary>
        private void ValidateBounds()
        {
            if (LowerBound > UpperBound)
            {
                throw new ArgumentException($"Lower bound ({LowerBound}) cannot be greater than upper bound ({UpperBound}) for variable {Name}");
            }

            // Type-specific validation
            switch (Type)
            {
                case VariableType.Binary:
                if (LowerBound < 0 || UpperBound > 1)
                {
                    throw new ArgumentException($"Binary variable {Name} must have bounds within [0, 1]");
                }
                break;

                case VariableType.Positive:
                if (LowerBound < 0)
                {
                    throw new ArgumentException($"Positive variable {Name} cannot have negative lower bound");
                }
                break;

                case VariableType.Negative:
                if (UpperBound > 0)
                {
                    throw new ArgumentException($"Negative variable {Name} cannot have positive upper bound");
                }
                break;
            }
        }

        /// <summary>
        /// Checks if the current value satisfies the variable's constraints
        /// </summary>
        /// <returns>True if valid, false otherwise</returns>
        public bool IsValidValue()
        {
            // Check bounds
            if (Value < LowerBound || Value > UpperBound)
                return false;

            // Check type-specific constraints
            switch (Type)
            {
                case VariableType.Integer:
                return Math.Abs(Value - Math.Round(Value)) < 1e-9; 

                case VariableType.Binary:
                return Math.Abs(Value) < 1e-9 || Math.Abs(Value - 1.0) < 1e-9;

                default:
                return true;
            }
        }

        /// <summary>
        /// Checks if this variable is integer-constrained (integer or binary)
        /// </summary>
        public bool IsIntegerConstrained => Type == VariableType.Integer || Type == VariableType.Binary;

        /// <summary>
        /// Gets the fractional part of the current value (for integer variables)
        /// </summary>
        public double FractionalPart => Value - Math.Floor(Value);

        /// <summary>
        /// Rounds the current value to satisfy integer constraints
        /// </summary>
        public void RoundToInteger()
        {
            if (IsIntegerConstrained)
            {
                Value = Math.Round(Value);

                // Ensure binary variables stay 0 or 1
                if (Type == VariableType.Binary)
                {
                    Value = Math.Max(0, Math.Min(1, Value));
                }
            }
        }

        /// <summary>
        /// Creates a copy of this variable
        /// </summary>
        public Variable Clone()
        {
            return new Variable
            {
                Index = this.Index,
                Name = this.Name,
                Coefficient = this.Coefficient,
                Type = this.Type,
                Value = this.Value,
                LowerBound = this.LowerBound,
                UpperBound = this.UpperBound,
                IsBasic = this.IsBasic
            };
        }

        /// <summary>
        /// String representation
        /// </summary>
        public override string ToString()
        {
            return $"{Name}: coeff={Coefficient:F3}, value={Value:F3}, type={Type}, bounds=[{LowerBound:F1}, {UpperBound:F1}]";
        }

        /// <summary>
        /// Formatted string for tableau display
        /// </summary>
        public string ToTableauString()
        {
            return $"{Name}({Coefficient:+0.000;-0.000})";
        }

        /// <summary>
        /// Equality comparison based on index
        /// </summary>
        public override bool Equals(object obj)
        {
            if (obj is Variable other)
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