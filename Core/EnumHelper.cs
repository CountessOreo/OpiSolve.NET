namespace OptiSolver.NET.Core
{
    /// <summary>
    /// Helper class for parsing and converting enum values
    /// </summary>
    public static class EnumHelper
    {
        /// <summary>
        /// Parses objective type from string (max/min)
        /// </summary>
        public static ObjectiveType ParseObjectiveType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Objective type cannot be null or empty");

            return value.ToLowerInvariant() switch
            {
                "max" => ObjectiveType.Maximize,
                "maximize" => ObjectiveType.Maximize,
                "min" => ObjectiveType.Minimize,
                "minimize" => ObjectiveType.Minimize,
                _ => throw new ArgumentException($"Invalid objective type '{value}'. Expected 'max' or 'min'")
            };
        }

        /// <summary>
        /// Parses constraint relation from string (<=, =, >=)
        /// </summary>
        public static ConstraintRelation ParseRelation(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Relation cannot be null or empty");

            var v = value.Trim();
            return v switch
            {
                "<=" or "≤" => ConstraintRelation.LessThanOrEqual,
                "=" => ConstraintRelation.Equal,
                ">=" or "≥" => ConstraintRelation.GreaterThanOrEqual,
                _ => throw new ArgumentException($"Invalid relation '{value}'. Expected '<=', '=', or '>=' (Unicode '≤' and '≥' are also accepted)")
            };
        }

        /// <summary>
        /// Parses variable type from string (+, -, urs, int, bin)
        /// </summary>
        public static VariableType ParseVariableType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Variable type cannot be null or empty");

            return value.ToLowerInvariant() switch
            {
                "+" => VariableType.Positive,
                "-" => VariableType.Negative,
                "urs" => VariableType.Unrestricted,
                "int" => VariableType.Integer,
                "bin" => VariableType.Binary,
                _ => throw new ArgumentException(
                            $"Invalid variable type '{value}'. Expected '+', '-', 'urs', 'int', or 'bin'")
            };
        }

        /// <summary>
        /// Converts constraint relation to string representation
        /// </summary>
        public static string RelationToString(ConstraintRelation relation)
        {
            return relation switch
            {
                ConstraintRelation.LessThanOrEqual => "<=",
                ConstraintRelation.Equal => "=",
                ConstraintRelation.GreaterThanOrEqual => ">=",
                _ => throw new ArgumentException($"Unknown constraint relation: {relation}")
            };
        }

        /// <summary>
        /// Converts objective type to string representation
        /// </summary>
        public static string ObjectiveTypeToString(ObjectiveType objectiveType)
        {
            return objectiveType switch
            {
                ObjectiveType.Maximize => "maximize",
                ObjectiveType.Minimize => "minimize",
                _ => throw new ArgumentException($"Unknown objective type: {objectiveType}")
            };
        }

        /// <summary>
        /// Converts variable type to string representation
        /// </summary>
        public static string VariableTypeToString(VariableType variableType)
        {
            return variableType switch
            {
                VariableType.Positive => "+",
                VariableType.Negative => "-",
                VariableType.Unrestricted => "urs",
                VariableType.Integer => "int",
                VariableType.Binary => "bin",
                _ => throw new ArgumentException($"Unknown variable type: {variableType}")
            };
        }

        /// <summary>
        /// Checks if a variable type represents an integer-constrained variable
        /// </summary>
        public static bool IsIntegerType(VariableType variableType)
        {
            return variableType == VariableType.Integer || variableType == VariableType.Binary;
        }

        /// <summary>
        /// Gets the default bounds for a variable type
        /// </summary>
        public static (double lowerBound, double upperBound) GetDefaultBounds(VariableType variableType)
        {
            return variableType switch
            {
                VariableType.Positive => (0.0, double.PositiveInfinity),
                VariableType.Negative => (double.NegativeInfinity, 0.0),
                VariableType.Unrestricted => (double.NegativeInfinity, double.PositiveInfinity),
                VariableType.Binary => (0.0, 1.0),
                VariableType.Integer => (0.0, double.PositiveInfinity),
                _ => throw new ArgumentException($"Unknown variable type: {variableType}")
            };
        }
    }
}