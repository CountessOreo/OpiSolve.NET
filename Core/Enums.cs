namespace OptiSolver.NET.Core
{
    /// <summary>
    /// Type of optimization objective
    /// </summary>
    public enum ObjectiveType
    {
        Maximize,
        Minimize
    }

    /// <summary>
    /// Type of constraint relationship
    /// </summary>
    public enum ConstraintRelation
    {
        LessThanOrEqual,    // <=
        Equal,              // =
        GreaterThanOrEqual  // >=
    }

    /// <summary>
    /// Variable type and restrictions
    /// </summary>
    public enum VariableType
    {
        Positive,       // x >= 0 (denoted as +)
        Negative,       // x <= 0 (denoted as -)
        Unrestricted,   // no sign restriction (denoted as urs)
        Integer,        // integer variable x >= 0 (denoted as int)
        Binary,         // binary variable x ∈ {0,1} (denoted as bin)
    }

    /// <summary>
    /// Solution status after solving
    /// </summary>
    public enum SolutionStatus
    {
        NotSolved,      // Model hasn't been solved yet
        Optimal,        // Optimal solution found
        Infeasible,     // No feasible solution exists
        Unbounded,      // Objective function is unbounded
        DegenerateCycle, // Cycling detected in simplex
        MaxIterations,  // Maximum iterations reached without convergence
        Error,          // Solver encountered an error
        IntegerOptimal, // Optimal integer solution found (for IP)
        IntegerInfeasible, // No integer feasible solution exists
        AlternativeOptimal  // Alternative solution found
    }

    /// <summary>
    /// Type of LP solver algorithm
    /// </summary>
    public enum SolverType
    {
        PrimalSimplex,
        RevisedSimplex,
        BranchAndBoundSimplex,
        CuttingPlaneSimplex,
        BranchAndBoundKnapsack
    }

    /// <summary>
    /// Phase of the simplex algorithm
    /// </summary>
    public enum SimplexPhase
    {
        PhaseI,     // Finding initial feasible solution
        PhaseII     // Optimizing objective function
    }

    /// <summary>
    /// Type of pivot operation in simplex tableau
    /// </summary>
    public enum PivotType
    {
        Entering,   // Variable entering the basis
        Leaving,    // Variable leaving the basis
        Optimal,    // Optimal solution reached
        Unbounded,  // Unbounded solution detected
        Degenerate  // Degenerate pivot (tie in minimum ratio test)
    }

    /// <summary>
    /// Branching strategy for Branch and Bound
    /// </summary>
    public enum BranchingStrategy
    {
        FirstFractional,        // Branch on first fractional variable found
        MostFractional,         // Branch on variable closest to 0.5
        LeastFractional,        // Branch on variable closest to integer
        LargestAbsObjectiveCoef  // Branch on variable with highest objective coefficient
    }

    /// <summary>
    /// Node exploration strategy for Branch and Bound
    /// </summary>
    public enum NodeSelectionStrategy
    {
        DepthFirst,     // Explore deepest nodes first
        BreadthFirst,   // Explore all nodes at current level first
        BestFirst       // Explore node with best bound first
    }

    /// <summary>
    /// Type of cut in cutting plane method
    /// </summary>
    public enum CutType
    {
        Gomory         // Gomory fractional cut
    }

    /// <summary>
    /// Types of canonical variables
    /// </summary>
    public enum CanonicalVariableType
    {
        Regular,                    // Direct mapping from original (sign +1)
        UnrestrictedPositive,       // Positive part of unrestricted variable (x+)
        UnrestrictedNegative,       // Negative part of unrestricted variable (x-)
        NegativeSubstitution        // Substituted var y where x = -y (for x ≤ 0)
    }

    /// <summary>
    /// Component of split unrestricted variable
    /// </summary>
    public enum VariableSplitComponent
    {
        None,       // Not a split variable
        Positive,   // Positive component (x+)
        Negative    // Negative component (x-)
    }

    /// <summary>
    /// Types of auxiliary variables
    /// </summary>
    public enum AuxiliaryVariableType
    {
        Slack,      // For <= constraints
        Surplus,    // For >= constraints
        Artificial  // For = and >= constraints (Phase I)
    }
}