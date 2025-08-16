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
        Error,
        MaxIterationsReached
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
        /// Converts constraint relation to string symbol
        /// </summary>
        public static string RelationToString(ConstraintRelation relation)
        {
            return relation switch
            {
                ConstraintRelation.LessThanOrEqual => "<=",
                ConstraintRelation.Equal => "=",
                ConstraintRelation.GreaterThanOrEqual => ">=",
                _ => "?"
            };
        }

        /// <summary>
        /// Converts string symbol to constraint relation
        /// </summary>
        public static ConstraintRelation StringToRelation(string relationString)
        {
            return relationString?.Trim() switch
            {
                "<=" => ConstraintRelation.LessThanOrEqual,
                "=" => ConstraintRelation.Equal,
                ">=" => ConstraintRelation.GreaterThanOrEqual,
                _ => throw new ArgumentException($"Unknown constraint relation: {relationString}")
            };
        }

        /// <summary>
        /// Converts objective type to display string
        /// </summary>
        public static string ObjectiveTypeToString(ObjectiveType type)
        {
            return type switch
            {
                ObjectiveType.Maximize => "Maximize",
                ObjectiveType.Minimize => "Minimize",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Converts string to objective type
        /// </summary>
        public static ObjectiveType StringToObjectiveType(string objectiveString)
        {
            return objectiveString?.ToLower().Trim() switch
            {
                "max" or "maximize" => ObjectiveType.Maximize,
                "min" or "minimize" => ObjectiveType.Minimize,
                _ => throw new ArgumentException($"Unknown objective type: {objectiveString}")
            };
        }

        /// <summary>
        /// Converts variable type to display string
        /// </summary>
        public static string VariableTypeToString(VariableType type)
        {
            return type switch
            {
                VariableType.Positive => "Positive",
                VariableType.Negative => "Negative",
                VariableType.Unrestricted => "Unrestricted",
                VariableType.Binary => "Binary",
                VariableType.Integer => "Integer",
                VariableType.Continuous => "Continuous",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Converts string to variable type
        /// </summary>
        public static VariableType StringToVariableType(string variableString)
        {
            return variableString?.ToLower().Trim() switch
            {
                "+" or "pos" or "positive" => VariableType.Positive,
                "-" or "neg" or "negative" => VariableType.Negative,
                "urs" or "unrestricted" => VariableType.Unrestricted,
                "bin" or "binary" => VariableType.Binary,
                "int" or "integer" => VariableType.Integer,
                "cont" or "continuous" => VariableType.Continuous,
                _ => throw new ArgumentException($"Unknown variable type: {variableString}")
            };
        }

        /// <summary>
        /// Converts solution status to display string
        /// </summary>
        public static string SolutionStatusToString(SolutionStatus status)
        {
            return status switch
            {
                SolutionStatus.NotSolved => "Not Solved",
                SolutionStatus.Optimal => "Optimal",
                SolutionStatus.Infeasible => "Infeasible",
                SolutionStatus.Unbounded => "Unbounded",
                SolutionStatus.Error => "Error",
                SolutionStatus.MaxIterationsReached => "Max Iterations Reached",
                SolutionStatus.Alternative => "Alternative Solution",
                _ => "Unknown"
            };
        }
    }
}
