# Create a structured class responsibilities document
class_descriptions = """
# OptiSolve.NET – Class Responsibilities Overview

This document provides a brief description of each class in the OptiSolve.NET project to help guide development within your group.

---

## 📁 Controller/

### SolverController.cs
- Orchestrates the solving process based on menu selections.
- Calls appropriate solver classes depending on algorithm choice.
- Routes between input parsing, solving, output writing, and display.

---

## 📁 Core/

### LPModel.cs
- Represents the entire LP/IP model.
- Stores objective type, list of variables, and list of constraints.

### Variable.cs
- Represents a single decision variable.
- Stores index, name, coefficient in the objective function, and type (binary, integer, etc.).

### Constraint.cs
- Represents a single constraint in the model.
- Stores LHS coefficients, relation type (<=, =, >=), and RHS value.

### Enum.cs
- Contains enumerations used across the app:
  - ObjectiveType (Maximize, Minimize)
  - ConstraintRelation (<=, =, >=)
  - VariableType (Continuous, Integer, Binary, Unrestricted)

---

## 📁 IO/

### InputParser.cs
- Reads and parses input files according to the required format.
- Builds and returns an LPModel object.
- Handles formatting errors and validation.

### OutputWriter.cs
- Writes solver results to output files.
- Includes canonical form, all iteration steps, and final solution.
- Rounds all numeric values to 3 decimal places.

---

## 📁 Services/

### SimplexSolver.cs
- Solves LP problems using the Primal Simplex Algorithm.
- Displays canonical form and full tableau iterations.

### RevisedSimplexSolver.cs
- Solves LPs using the Revised Primal Simplex Algorithm.
- Uses matrix/vector operations and displays product form and price-out steps.

### BranchBoundSolver.cs
- Solves Integer Programming models using Branch and Bound.
- Creates and solves subproblems, applies backtracking, and selects the best candidate.

### CuttingPlaneSolver.cs
- Solves IPs using the Cutting Plane method.
- Adds cuts to eliminate non-integer solutions and displays iterations.

### KnapsackSolver.cs
- Specialized solver for binary Knapsack problems using Branch and Bound.

### DualSolver.cs
- Constructs and solves the dual of a given LP model.
- Verifies strong/weak duality.

### SensitivityAnalyser.cs
- Performs sensitivity analysis:
  - Variable ranges and RHS variation
  - Shadow prices
  - Adds constraints and activities to optimal solutions

---

## 📁 UI/

### Menu.cs
- Displays the main menu and gets user input.
- Sends control to the SolverController.

### DisplayHelper.cs
- Formats and displays solver results to the console.
- Utility for clean and consistent output formatting.

---

## 📄 Program.cs
- Entry point of the application.
- Initializes menu system and starts the app loop.

---

Make sure each class only handles its own responsibilities. This makes the system modular, testable, and easier to debug or extend.
"""

# Write to file
output_path = Path("/mnt/data/OptiSolve_ClassResponsibilities.txt")
output_path.write_text(class_descriptions)

output_path.name
