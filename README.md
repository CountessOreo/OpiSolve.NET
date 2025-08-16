# OptiSolve.NET

**A Linear and Integer Programming Solver with Sensitivity Analysis, Built in C#**

OptiSolve.NET is a console-based optimization tool developed using the .NET framework. It provides a menu-driven interface to solve a wide range of optimization problems including Linear Programming (LP) and Integer Programming (IP) using classical algorithms like the Primal Simplex, Revised Simplex, Branch & Bound, and Cutting Plane methods. The tool also includes a comprehensive sensitivity analysis module.

## Features

- Solve both LP and IP problems, including:
  - Maximization and Minimization models
  - Binary and Integer constraints
- Supports the following algorithms:
  - Primal Simplex Algorithm
  - Revised Primal Simplex Algorithm
  - Branch & Bound Simplex Algorithm
  - Cutting Plane Algorithm
  - Branch & Bound Knapsack Algorithm
- Menu-driven interface
- Canonical form display and full iteration output
- Sensitivity analysis tools:
  - Ranges and impacts for basic/non-basic variables
  - Shadow price display
  - Duality analysis and dual model solving
- Custom input file format with flexible number of variables and constraints
- Generates detailed output text files with step-by-step solutions

## Requirements

- .NET 6.0 SDK or later
- Visual Studio 2022 or later (or any C# IDE)
- Windows 10/11 recommended


## Project Structure
```
OptiSolver.NET/
├── Analysis/                           # Sensitivity Analysis Components
│   ├── DualityAnalyser.cs             # Dual model analysis and verification
│   ├── RangeAnalyser.cs               # Variable and constraint range analysis
│   ├── SensitivityAnalyser.cs         # Main sensitivity analysis coordinator
│   └── ShadowPriceCalculator.cs       # Shadow price calculations
├── Controller/
│   └── SolverController.cs            # Main application orchestration
├── Core/                              # Fundamental Data Models
│   ├── Constraint.cs                  # Constraint representation
│   ├── Enums.cs                       # Enumerations and constants
│   ├── LPModel.cs                     # Linear programming model
│   └── Variable.cs                    # Decision variable representation
├── Exceptions/                        # Custom Exception Handling
│   ├── AlgorithmException.cs          # General algorithm errors
│   ├── InfeasibleSolutionException.cs # Infeasible solution detection
│   ├── InvalidInputException.cs       # Input validation errors
│   └── UnboundedSolutionException.cs  # Unbounded solution detection
├── IO/                               # Input/Output Operations
│   ├── InputParser.cs                # Parse input files
│   └── OutputWriter.cs               # Generate output files
├── Models/                           # Complex Data Structures
│   ├── Analysis/
│   │   ├── DualSolution.cs           # Dual problem solution
│   │   ├── SensitivityRange.cs       # Sensitivity range data
│   │   └── ShadowPrice.cs            # Shadow price information
│   ├── Solution/
│   │   ├── IterationResult.cs        # Individual iteration data
│   │   ├── OptimalSolution.cs        # Final optimal solution
│   │   └── SolutionStatus.cs         # Solution status information
│   └── Tableau/
│       ├── SimplexTableau.cs         # Simplex tableau representation
│       ├── TableauColumn.cs          # Tableau column operations
│       └── TableauRow.cs             # Tableau row operations
├── Services/                         # Algorithm Implementations
│   ├── Base/
│   │   ├── ISolver.cs                # Base solver interface
│   │   ├── SolutionResult.cs         # Generic solution result
│   │   └── SolverBase.cs             # Common solver functionality
│   ├── BranchAndBound/
│   │   ├── BranchBoundKnapsack.cs    # Branch & Bound for Knapsack
│   │   ├── BranchBoundSimplex.cs     # Branch & Bound with Simplex
│   │   └── BranchNode.cs             # Branch tree node representation
│   │   ├── KnapsackItem.cs           # Knapsack item representation
│   │   ├── KnapsackNode.cs           # Knapsack node representation
│   │   ├── KnapsackSolverWrapper.cs  # Knapsack solver algorithm
│   │   └── BranchAndBoundKnapsack.cs # Knapsack-specific solver 
│   ├── CuttingPlane/
│   │   ├── CuttingPlaneSolver.cs     # Cutting plane algorithm
│   │   └── CuttingPlaneTableau.cs    # Cutting plane tableau
│   └── Simplex/
│       ├── RevisedSimplexSolver.cs   # Revised simplex implementation
│       ├── SimplexSolver.cs          # Standard simplex implementation
│       └── SimplexTableau.cs         # Simplex tableau operations
├── Tests/                            # Testing Infrastructure
│   ├── TestData/
│   │   └── ExpectedOutputs/
│   │       ├── sample_ip.txt         # Sample integer programming input
│   │       └── sample_lp.txt         # Sample linear programming input
├── UI/                               # User Interface Components
│   ├── DisplayHelper.cs              # Output formatting utilities
│   └── Menu.cs                       # Menu system implementation
├── Utilities/                        # Common Utility Functions
│   ├── ErrorHandler.cs               # Centralized error handling
│   ├── MathHelper.cs                 # Mathematical operations
│   ├── MatrixOperations.cs           # Matrix manipulation utilities
│   └── Validator.cs                  # Input validation utilities
└── Program.cs                        # Application entry point
```


## Input File Format
```bash
max +2 +3 +3 +5 +2 +4
+11 +8 +6 +14 +10 +10 <=40
bin bin bin bin bin bin
```

### Explanation:
- **Line 1:** `max` or `min` followed by signs and coefficients for each decision variable in the objective function
- **Next lines:** Technological coefficients, relation symbol (`<=`, `=`, `>=`), and RHS value
- **Final line:** Sign restrictions (`+`, `-`, `urs`, `int`, `bin`) in variable order

## Output File Format

The program generates an output file that includes:
- Canonical form
- All iteration steps (tableaux or matrix steps)
- Final optimal solution
- Sensitivity analysis results
- Rounded values (3 decimal points)

## Usage

1. Launch the executable:
    ```
    solve.exe
    ```

2. Choose from the menu options:
    ```
    1. Solve LP using Primal Simplex
    2. Solve LP using Revised Simplex
    3. Solve IP using Branch & Bound
    4. Solve IP using Cutting Plane
    5. Solve Knapsack IP
    6. Perform Sensitivity Analysis
    7. Solve Dual
    8. Exit
    ```

3. Provide the input file when prompted.
4. View and analyze the output file generated.


## Algorithm Implementations

### Simplex Methods

- Primal Simplex: Standard tableau-based implementation with full iteration display
- Revised Simplex: Matrix-based approach with Product Form and Price Out iterations

### Integer Programming

- Branch & Bound Simplex: Complete tree traversal with backtracking and fathoming
- Branch & Bound Knapsack: Specialized knapsack implementation with optimal branching
- Cutting Plane: Gomory cuts with iterative constraint addition

### Key Features

- Backtracking: Implemented in all Branch & Bound algorithms
- Fathoming: Automatic pruning of suboptimal branches
- Canonical Form: Automatic conversion and display for all algorithms
- Iteration Tracking: Complete step-by-step solution process

### Sensitivity Analysis Options

- Range of selected Basic/Non-Basic Variables
- Apply coefficient or RHS changes
- Add a new activity or constraint
- Show shadow prices
- Solve and verify Dual Model
- Strong/Weak Duality verification

### Special Case Handling

- Detects and reports:
    - Infeasible models
    - Unbounded solutions
    - Invalid input formats
    - Algorithm-specific errors

### Data Flow

- Input: InputParser reads and validates model files
- Processing: SolverController orchestrates algorithm execution
- Analysis: Sensitivity analysis performed on optimal solutions
- Output: OutputWriter generates formatted result files

### Bonus Feature

Supports solving basic non-linear functions like `f(x) = x²` using iterative optimization (e.g., gradient descent), if enabled.

## Authors

This project was completed by:
- Hayley Treutens @CountessOreo
- 
- 
- 

## Screenshots



