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

## Sensitivity Analysis Options

- Range of selected Basic/Non-Basic Variables
- Apply coefficient or RHS changes
- Add a new activity or constraint
- Show shadow prices
- Solve and verify Dual Model
- Strong/Weak Duality verification

## Special Case Handling

- Detects and reports:
  - Infeasible models
  - Unbounded solutions
  - Invalid input formats

## Bonus Feature

Supports solving basic non-linear functions like `f(x) = x²` using iterative optimization (e.g., gradient descent), if enabled.

## Authors

This project was completed by:
- Hayley Treutens @CountessOreo
- 
- 
- 

## Screenshots



