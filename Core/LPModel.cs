using System.Text;

namespace OptiSolver.NET.Core
{
    /// <summary>
    /// Represents a complete Linear Programming or Integer Programming model
    /// Contains objective function, decision variables, and constraints
    /// </summary>
    public class LPModel
    {
        /// Name of the model (for display purposes)
        public string Name { get; set; }

        /// Whether this is a maximization or minimization problem
        public ObjectiveType ObjectiveType { get; set; }

        /// List of all decision variables in the model
        public List<Variable> Variables { get; set; }

        /// List of all constraints in the model
        public List<Constraint> Constraints { get; set; }

        /// Current optimal objective value (set after solving)
        public double ObjectiveValue { get; set; }

        /// Status of the model after solving
        public SolutionStatus Status { get; set; }

        /// Whether this model contains integer variables (IP vs LP)
        public bool IsIntegerProgram => Variables?.Any(v => v.IsIntegerConstrained) ?? false;

        /// Whether this model contains only binary variables
        public bool IsBinaryProgram => Variables?.All(v => v.Type == VariableType.Binary) ?? false;

        /// Number of decision variables
        public int VariableCount => Variables?.Count ?? 0;

        /// Number of constraints
        public int ConstraintCount => Constraints?.Count ?? 0;

        /// <summary>
        /// Default constructor
        /// </summary>
        public LPModel()
        {
            Name = "LP Model";
            ObjectiveType = ObjectiveType.Maximize;
            Variables = new List<Variable>();
            Constraints = new List<Constraint>();
            ObjectiveValue = 0.0;
            Status = SolutionStatus.NotSolved;
        }

        /// <summary>
        /// Constructor with basic parameters
        /// </summary>
        /// <param name="name">Model name</param>
        /// <param name="objectiveType">Maximize or minimize</param>
        public LPModel(string name, ObjectiveType objectiveType)
        {
            Name = name ?? "LP Model";
            ObjectiveType = objectiveType;
            Variables = new List<Variable>();
            Constraints = new List<Constraint>();
            ObjectiveValue = 0.0;
            Status = SolutionStatus.NotSolved;
        }

        /// <summary>
        /// Full constructor
        /// </summary>
        public LPModel(string name, ObjectiveType objectiveType, List<Variable> variables, List<Constraint> constraints)
        {
            Name = name ?? "LP Model";
            ObjectiveType = objectiveType;
            Variables = new List<Variable>(variables ?? new List<Variable>());
            Constraints = new List<Constraint>(constraints ?? new List<Constraint>());
            ObjectiveValue = 0.0;
            Status = SolutionStatus.NotSolved;

            ValidateModel();
        }

        #region Variable Management

        /// <summary>
        /// Adds a new variable to the model
        /// </summary>
        /// <param name="coefficient">Objective function coefficient</param>
        /// <param name="type">Variable type</param>
        /// <returns>The created variable</returns>
        public Variable AddVariable(double coefficient, VariableType type)
        {
            var variable = new Variable(Variables.Count, coefficient, type);
            Variables.Add(variable);

            // Extend all constraint coefficient lists to include this new variable
            foreach (var constraint in Constraints)
            {
                constraint.Coefficients.Add(0.0);
            }
            return variable;
        }

        /// <summary>
        /// Adds a variable with a specific name
        /// </summary>
        public Variable AddVariable(string name, double coefficient, VariableType type)
        {
            var variable = new Variable(Variables.Count, name, coefficient, type);
            Variables.Add(variable);

            // Extend constraint coefficients
            foreach (var constraint in Constraints)
            {
                constraint.Coefficients.Add(0.0);
            }
            return variable;
        }

        /// <summary>
        /// Gets a variable by index
        /// </summary>
        public Variable GetVariable(int index)
        {
            if (index < 0 || index >= Variables.Count)
                throw new ArgumentOutOfRangeException(nameof(index), $"Variable index {index} is out of range [0, {Variables.Count - 1}]");

            return Variables[index];
        }

        /// <summary>
        /// Gets a variable by name
        /// </summary>
        public Variable GetVariable(string name)
        {
            var variable = Variables.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (variable == null)
                throw new ArgumentException($"Variable with name '{name}' not found");

            return variable;
        }

        #endregion

        #region Constraint Management

        /// <summary>
        /// Adds a new constraint to the model
        /// </summary>
        /// <param name="coefficients">LHS coefficients for each variable</param>
        /// <param name="relation">Constraint relation</param>
        /// <param name="rhs">Right-hand side value</param>
        /// <returns>The created constraint</returns>
        public Constraint AddConstraint(List<double> coefficients, ConstraintRelation relation, double rhs)
        {
            // Ensure coefficients list has the right size
            var adjustedCoeffs = new List<double>(coefficients ?? new List<double>());
            while (adjustedCoeffs.Count < Variables.Count)
                adjustedCoeffs.Add(0.0);

            var constraint = new Constraint(Constraints.Count, adjustedCoeffs, relation, rhs);
            Constraints.Add(constraint);
            return constraint;
        }

        /// <summary>
        /// Adds a constraint with double array
        /// </summary>
        public Constraint AddConstraint(double[] coefficients, ConstraintRelation relation, double rhs)
        {
            return AddConstraint(coefficients?.ToList(), relation, rhs);
        }

        /// <summary>
        /// Adds a constraint with a specific name
        /// </summary>
        public Constraint AddConstraint(string name, List<double> coefficients, ConstraintRelation relation, double rhs)
        {
            var constraint = AddConstraint(coefficients, relation, rhs);
            constraint.Name = name;
            return constraint;
        }

        /// <summary>
        /// Gets a constraint by index
        /// </summary>
        public Constraint GetConstraint(int index)
        {
            if (index < 0 || index >= Constraints.Count)
                throw new ArgumentOutOfRangeException(nameof(index), $"Constraint index {index} is out of range [0, {Constraints.Count - 1}]");

            return Constraints[index];
        }

        #endregion

        #region Model Validation and Analysis

        /// <summary>
        /// Validates the model for consistency and correctness
        /// </summary>
        public void ValidateModel()
        {
            var errors = new List<string>();

            // Check if we have variables
            if (Variables == null || Variables.Count == 0)
                errors.Add("Model must have at least one variable");

            // Check if we have constraints (optional but usually expected)
            if (Constraints == null)
                errors.Add("Constraints list cannot be null");

            // Validate constraint coefficient dimensions
            if (Constraints != null)
            {
                for (int i = 0; i < Constraints.Count; i++)
                {
                    var constraint = Constraints[i];
                    if (constraint.Coefficients.Count != Variables.Count)
                    {
                        errors.Add($"Constraint {i} has {constraint.Coefficients.Count} coefficients but model has {Variables.Count} variables");
                    }
                }
            }

            // Check for duplicate variable names
            if (Variables != null)
            {
                var duplicateNames = Variables.GroupBy(v => v.Name)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key);

                foreach (var dupName in duplicateNames)
                {
                    errors.Add($"Duplicate variable name: {dupName}");
                }
            }

            if (errors.Any())
            {
                throw new InvalidOperationException("Model validation failed:\n" + string.Join("\n", errors));
            }
        }

        /// <summary>
        /// Calculates the objective value for given variable values
        /// </summary>
        /// <param name="variableValues">Values for each variable</param>
        /// <returns>Objective function value</returns>
        public double CalculateObjectiveValue(double[] variableValues)
        {
            if (variableValues == null)
                throw new ArgumentNullException(nameof(variableValues));

            if (variableValues.Length != Variables.Count)
                throw new ArgumentException($"Expected {Variables.Count} variable values, got {variableValues.Length}");

            double objValue = 0.0;
            for (int i = 0; i < Variables.Count; i++)
            {
                objValue += Variables[i].Coefficient * variableValues[i];
            }

            return objValue;
        }

        /// <summary>
        /// Calculates objective value using current variable values
        /// </summary>
        public double CalculateCurrentObjectiveValue()
        {
            double objValue = 0.0;
            foreach (var variable in Variables)
            {
                objValue += variable.Coefficient * variable.Value;
            }
            return objValue;
        }

        /// <summary>
        /// Checks if all constraints are satisfied for given variable values
        /// </summary>
        public bool IsFeasible(double[] variableValues, double tolerance = 1e-9)
        {
            return Constraints.All(c => c.IsSatisfied(variableValues, tolerance));
        }

        /// <summary>
        /// Checks feasibility using current variable values
        /// </summary>
        public bool IsCurrentSolutionFeasible(double tolerance = 1e-9)
        {
            return Constraints.All(c => c.IsSatisfied(Variables, tolerance));
        }

        /// <summary>
        /// Gets all constraint violations for given variable values
        /// </summary>
        public List<(int constraintIndex, double violation)> GetViolations(double[] variableValues)
        {
            var violations = new List<(int, double)>();

            for (int i = 0; i < Constraints.Count; i++)
            {
                double violation = Constraints[i].GetViolation(variableValues);
                if (violation > 1e-9)
                {
                    violations.Add((i, violation));
                }
            }

            return violations;
        }

        #endregion

        #region Model Transformations

        /// <summary>
        /// Converts the model to standard form (all constraints <=, all variables >= 0)
        /// </summary>
        public LPModel ToStandardForm()
        {
            var standardModel = new LPModel($"{Name} (Standard Form)", ObjectiveType);

            // Handle variables - split unrestricted variables into x+ and x-
            var variableMapping = new Dictionary<int, List<int>>();

            for (int i = 0; i < Variables.Count; i++)
            {
                var originalVar = Variables[i];
                variableMapping[i] = new List<int>();

                if (originalVar.Type == VariableType.Unrestricted)
                {
                    // Split into x+ and x- where x = x+ - x-
                    var xPlus = standardModel.AddVariable($"{originalVar.Name}+", originalVar.Coefficient, VariableType.Positive);
                    var xMinus = standardModel.AddVariable($"{originalVar.Name}-", -originalVar.Coefficient, VariableType.Positive);
                    variableMapping[i].AddRange(new[] { xPlus.Index, xMinus.Index });
                }
                else
                {
                    var newVar = standardModel.AddVariable(originalVar.Name, originalVar.Coefficient, originalVar.Type);
                    variableMapping[i].Add(newVar.Index);
                }
            }

            // Transform constraints
            foreach (var originalConstraint in Constraints)
            {
                var (coeffs, relation, rhs, multiplier) = originalConstraint.ToStandardForm();

                // Expand coefficients for split variables
                var expandedCoeffs = new List<double>();
                for (int i = 0; i < Variables.Count; i++)
                {
                    var newIndices = variableMapping[i];
                    if (newIndices.Count == 1)
                    {
                        // Regular variable
                        expandedCoeffs.Add(coeffs[i]);
                    }
                    else
                    {
                        // Unrestricted variable split into x+ and x-
                        expandedCoeffs.Add(coeffs[i]);   // coefficient for x+
                        expandedCoeffs.Add(-coeffs[i]);  // coefficient for x- 
                    }
                }
                standardModel.AddConstraint($"{originalConstraint.Name}_std", expandedCoeffs, relation, rhs);
            }
            return standardModel;
        }

        /// <summary>
        /// Converts maximization to minimization (or vice versa)
        /// </summary>
        public void ConvertObjectiveType()
        {
            // Flip objective type
            ObjectiveType = ObjectiveType == ObjectiveType.Maximize ? ObjectiveType.Minimize : ObjectiveType.Maximize;

            // Negate all objective coefficients
            foreach (var variable in Variables)
            {
                variable.Coefficient = -variable.Coefficient;
            }

            // Negate current objective value if it's set
            ObjectiveValue = -ObjectiveValue;
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Creates a deep copy of this model
        /// </summary>
        public LPModel Clone()
        {
            var clonedModel = new LPModel(Name, ObjectiveType)
            {
                ObjectiveValue = this.ObjectiveValue,
                Status = this.Status
            };

            // Clone variables
            foreach (var variable in Variables)
            {
                clonedModel.Variables.Add(variable.Clone());
            }

            // Clone constraints
            foreach (var constraint in Constraints)
            {
                clonedModel.Constraints.Add(constraint.Clone());
            }

            return clonedModel;
        }

        /// <summary>
        /// Resets all variable values and solution status
        /// </summary>
        public void ResetSolution()
        {
            foreach (var variable in Variables)
            {
                variable.Value = 0.0;
                variable.IsBasic = false;
            }

            foreach (var constraint in Constraints)
            {
                constraint.SlackValue = 0.0;
                constraint.ShadowPrice = 0.0;
                constraint.IsActive = false;
            }

            ObjectiveValue = 0.0;
            Status = SolutionStatus.NotSolved;
        }

        #endregion

        #region Display Methods

        /// <summary>
        /// String representation of the complete model
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Model: {Name}");
            sb.AppendLine($"Type: {(IsIntegerProgram ? "Integer Programming" : "Linear Programming")} ({ObjectiveType})");
            sb.AppendLine($"Variables: {VariableCount}, Constraints: {ConstraintCount}");

            if (Status != SolutionStatus.NotSolved)
            {
                sb.AppendLine($"Status: {Status}");
                if (Status == SolutionStatus.Optimal)
                {
                    sb.AppendLine($"Objective Value: {ObjectiveValue:F3}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Detailed model formulation as string
        /// </summary>
        public string ToFormulationString()
        {
            var sb = new StringBuilder();

            // Objective function
            sb.AppendLine($"{ObjectiveType.ToString().ToUpper()}:");
            sb.Append("  ");
            for (int i = 0; i < Variables.Count; i++)
            {
                if (i > 0 && Variables[i].Coefficient >= 0)
                    sb.Append(" + ");
                else if (i > 0)
                    sb.Append(" ");

                sb.Append($"{Variables[i].Coefficient:F1}{Variables[i].Name}");
            }
            sb.AppendLine();

            // Constraints
            if (Constraints.Count > 0)
            {
                sb.AppendLine("\nSUBJECT TO:");
                foreach (var constraint in Constraints)
                {
                    sb.AppendLine($"  {constraint.ToTableauString(Variables)}");
                }
            }

            // Variable bounds
            sb.AppendLine("\nVARIABLE TYPES:");
            foreach (var variable in Variables)
            {
                sb.AppendLine($"  {variable.Name}: {variable.Type}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Current solution as string
        /// </summary>
        public string ToSolutionString()
        {
            if (Status == SolutionStatus.NotSolved)
                return "Model has not been solved yet.";

            var sb = new StringBuilder();
            sb.AppendLine($"Solution Status: {Status}");

            if (Status == SolutionStatus.Optimal)
            {
                sb.AppendLine($"Objective Value: {ObjectiveValue:F3}");
                sb.AppendLine("\nVariable Values:");
                foreach (var variable in Variables)
                {
                    sb.AppendLine($"  {variable.Name} = {variable.Value:F3}");
                }
            }
            else if (Status == SolutionStatus.Infeasible)
            {
                sb.AppendLine("No feasible solution exists.");
            }
            else if (Status == SolutionStatus.Unbounded)
            {
                sb.AppendLine("The objective function is unbounded.");
            }

            return sb.ToString();
        }

        #endregion
    }
}
