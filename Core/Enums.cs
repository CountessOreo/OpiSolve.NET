namespace OptiSolver.NET.Core
{
    /// <summary>
    /// Defines whether the optimization problem is a maximization or minimization problem
    /// </summary>
    public enum ObjectiveType 
    { 
        Maximize, 
        Minimize 
    }

    /// <summary>
    /// Defines the relationship type for constraints (<=, =, >=)
    /// </summary>
    public enum ConstraintRelation 
    { 
        LessThanOrEqual, 
        Equal, 
        GreaterThanOrEqual 
    }

    /// <summary>
    /// Defines the type and restrictions on decision variables
    /// </summary>
    public enum VariableType 
    { 
        Continuous, 
        Integer, 
        Binary, 
        Unrestricted,
        Positive,
        Negative
    }

    /// <summary>
    /// Represents the status of a solution after solving
    /// </summary>
    public enum SolutionStatus 
    { 
        NotSolved,
        Optimal, 
        Infeasible, 
        Unbounded,  
        Alternative,
        Error
    }

    /// <summary>
    /// Defines which algorithm to use for solving
    /// </summary>
    public enum SolverType
    {
        PrimalSimplex,
        RevisedSimplex,
        BranchAndBoundSimplex,
        BranchAndBoundKnapsack,
        CuttingPlane
    }

    /// <summary>
    /// Helper class to convert between string representations and enums
    /// </summary>
    public static class EnumHelper
    {
        /// <summary>
        /// Converts string relation to enum (<=, =, >=)
        /// </summary>
        public static ConstraintRelation ParseRelation(string relation)
        {
            return relation.Trim() switch
            {
                "<=" => ConstraintRelation.LessThanOrEqual,
                "=" => ConstraintRelation.Equal,
                ">=" => ConstraintRelation.GreaterThanOrEqual,
                _ => throw new ArgumentException($"Invalid constraint relation: {relation}")
            };
        }

        /// <summary>
        /// Converts string variable type to enum (+, -, urs, int, bin)
        /// </summary>
        public static VariableType ParseVariableType(string type)
        {
            return type.Trim().ToLower() switch
            {
                "+" => VariableType.Positive,
                "-" => VariableType.Negative,
                "urs" => VariableType.Unrestricted,
                "int" => VariableType.Integer,
                "bin" => VariableType.Binary,
                _ => throw new ArgumentException($"Invalid variable type: {type}")
            };
        }

        /// <summary>
        /// Converts string objective type to enum (max, min)
        /// </summary>
        public static ObjectiveType ParseObjectiveType(string objective)
        {
            return objective.Trim().ToLower() switch
            {
                "max" => ObjectiveType.Maximize,
                "min" => ObjectiveType.Minimize,
                _ => throw new ArgumentException($"Invalid objective type: {objective}")
            };
        }

        /// <summary>
        /// Converts relation enum back to string representation
        /// </summary>
        public static string RelationToString(ConstraintRelation relation)
        {
            return relation switch
            {
                ConstraintRelation.LessThanOrEqual => "<=",
                ConstraintRelation.Equal => "=",
                ConstraintRelation.GreaterThanOrEqual => ">=",
                _ => throw new ArgumentException($"Unknown relation: {relation}")
            };
        }

        /// <summary>
        /// Converts variable type enum back to string representation
        /// </summary>
        public static string VariableTypeToString(VariableType type)
        {
            return type switch
            {
                VariableType.Positive => "+",
                VariableType.Negative => "-",
                VariableType.Unrestricted => "urs",
                VariableType.Integer => "int",
                VariableType.Binary => "bin",
                VariableType.Continuous => "+", 
                _ => throw new ArgumentException($"Unknown variable type: {type}")
            };
        }
    }
}
