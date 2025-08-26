using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OptiSolver.NET.Core
{
    /// <summary>
    /// Represents an LP model in canonical/standard form for simplex algorithms
    /// In canonical form, variables are >= 0 and each row is modeled as an equality
    /// once slack/surplus/artificial variables are added (the underlying source relation
    /// may have been <=, =, or >= before canonicalization).
    /// </summary>
    public class CanonicalForm
    {
        // Original model name
        public string ModelName { get; set; }

        // Objective type (max/min) - same as original
        public ObjectiveType ObjectiveType { get; set; }

        // Objective function coefficients (including auxiliary variables with 0 coefficients)
        public double[] ObjectiveCoefficients { get; set; }

        // Constraint coefficient matrix A  [ConstraintCount x TotalVariables]
        public double[,] ConstraintMatrix { get; set; }

        // Right-hand side values (all >= 0 after canonicalization)
        public double[] RightHandSide { get; set; }

        // Variable bounds - all variables are >= 0 in canonical form
        public double[] LowerBounds { get; set; }

        // Variable upper bounds (most are +infinity)
        public double[] UpperBounds { get; set; }

        // Variable types in canonical form (for IP)
        public VariableType[] VariableTypes { get; set; }

        // Mapping between original and canonical variables
        public VariableMapping VariableMapping { get; set; }

        // Number of constraints in canonical form
        public int ConstraintCount { get; set; }

        // Total number of variables (original + auxiliary)
        public int TotalVariables { get; set; }

        // Number of original decision variables
        public int OriginalVariables { get; set; }

        // Whether this requires Phase I (has artificial variables)
        public bool RequiresPhaseI { get; set; }

        // Indices of artificial variables (for Phase I)
        public List<int> ArtificialVariableIndices { get; set; }

        // Phase I objective coefficients (for artificial variables)
        public double[] PhaseIObjective { get; set; }

        // Whether this is an integer program
        public bool IsIntegerProgram => VariableTypes?.Any(vt => EnumHelper.IsIntegerType(vt)) ?? false;

        public int OriginalVariableCount
        {
            get => OriginalVariables;
            set => OriginalVariables = value;
        }

        public CanonicalForm()
        {
            ArtificialVariableIndices = new List<int>();
            RequiresPhaseI = false;
        }


        /// <summary>
        /// Gets constraint as string for display
        /// </summary>
        public string GetConstraintString(int constraintIndex, bool includeSlack = true)
        {
            if (constraintIndex < 0 || constraintIndex >= ConstraintCount)
                throw new ArgumentOutOfRangeException(nameof(constraintIndex));

            var sb = new StringBuilder();
            bool first = true;

            for (int j = 0; j < TotalVariables; j++)
            {
                double coeff = ConstraintMatrix[constraintIndex, j];

                // Skip zeros; unless we want to show slack/aux explicitly
                if (Math.Abs(coeff) < 1e-10 && (!includeSlack || !VariableMapping.IsAuxiliaryVariable(j)))
                    continue;

                if (!first)
                    sb.Append(coeff >= 0 ? " + " : " ");
                else
                {
                    if (coeff < 0)
                        sb.Append("-");
                    first = false;
                }

                double absCoeff = Math.Abs(coeff);
                if (Math.Abs(absCoeff - 1.0) > 1e-10)
                    sb.Append($"{absCoeff:F3}");

                sb.Append(VariableMapping.GetCanonicalVariableName(j));
            }

            var rel = includeSlack ? "=" : "≤";
            sb.Append($" {rel} {RightHandSide[constraintIndex]:F3}");
            return sb.ToString();
        }

        /// <summary>
        /// Gets objective function as string for display
        /// </summary>
        public string GetObjectiveString(bool phaseI = false)
        {
            var coeffs = phaseI && PhaseIObjective != null ? PhaseIObjective : ObjectiveCoefficients;
            var objType = phaseI ? "MIN" : ObjectiveType.ToString().ToUpper();

            var sb = new StringBuilder($"{objType} z = ");
            bool first = true;

            for (int j = 0; j < TotalVariables; j++)
            {
                double coeff = coeffs[j];
                if (Math.Abs(coeff) < 1e-10)
                    continue;

                if (!first)
                    sb.Append(coeff >= 0 ? " + " : " ");
                else
                {
                    if (coeff < 0)
                        sb.Append("-");
                    first = false;
                }

                double absCoeff = Math.Abs(coeff);
                if (Math.Abs(absCoeff - 1.0) > 1e-10)
                    sb.Append($"{absCoeff:F3}");

                sb.Append(VariableMapping.GetCanonicalVariableName(j));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Creates Phase I model for artificial variables
        /// </summary>
        public CanonicalForm CreatePhaseIModel()
        {
            if (!RequiresPhaseI)
                return this;

            var phaseI = new CanonicalForm
            {
                ModelName = $"{ModelName} (Phase I)",
                ObjectiveType = ObjectiveType.Minimize, // Always minimize sum of artificials
                ConstraintMatrix = (double[,])ConstraintMatrix.Clone(),
                RightHandSide = (double[])RightHandSide.Clone(),
                LowerBounds = (double[])LowerBounds.Clone(),
                UpperBounds = (double[])UpperBounds.Clone(),
                VariableTypes = (VariableType[])VariableTypes.Clone(),
                VariableMapping = VariableMapping,
                ConstraintCount = ConstraintCount,
                TotalVariables = TotalVariables,
                OriginalVariables = OriginalVariables,
                RequiresPhaseI = false, // Phase I doesn't need another Phase I
                ArtificialVariableIndices = new List<int>(ArtificialVariableIndices)
            };

            // Phase I objective: minimize sum of artificial variables
            phaseI.PhaseIObjective = new double[TotalVariables];
            phaseI.ObjectiveCoefficients = new double[TotalVariables];

            foreach (int artificialIndex in ArtificialVariableIndices)
            {
                phaseI.PhaseIObjective[artificialIndex] = 1.0;
                phaseI.ObjectiveCoefficients[artificialIndex] = 0.0; // zero in original objective
            }

            return phaseI;
        }

        /// <summary>
        /// Gets initial basic feasible solution (if one exists)
        /// </summary>
        public (int[] basisIndices, double[] basicSolution) GetInitialBasicSolution()
        {
            var basisIndices = new int[ConstraintCount];
            var basicSolution = new double[TotalVariables];

            if (RequiresPhaseI)
            {
                for (int i = 0; i < ConstraintCount; i++)
                {
                    var artIdx = VariableMapping
                        .AuxiliaryVariables
                        .FirstOrDefault(kv => kv.Value.Type == AuxiliaryVariableType.Artificial &&
                                              kv.Value.ConstraintIndex == i).Key;

                    if (!VariableMapping.AuxiliaryVariables.ContainsKey(artIdx))
                        throw new InvalidOperationException($"No artificial variable found for constraint row {i}");

                    basisIndices[i] = artIdx;
                    basicSolution[artIdx] = RightHandSide[i];
                }
            }
            else
            {
                for (int i = 0; i < ConstraintCount; i++)
                {
                    var slackIdx = VariableMapping
                        .AuxiliaryVariables
                        .FirstOrDefault(kv => kv.Value.Type == AuxiliaryVariableType.Slack &&
                                              kv.Value.ConstraintIndex == i).Key;

                    if (!VariableMapping.AuxiliaryVariables.ContainsKey(slackIdx))
                        throw new InvalidOperationException($"No slack variable found for constraint row {i}");

                    basisIndices[i] = slackIdx;
                    basicSolution[slackIdx] = RightHandSide[i];
                }
            }

            return (basisIndices, basicSolution);
        }

        /// <summary>
        /// Validates the canonical form for correctness
        /// </summary>
        public void Validate()
        {
            var errors = new List<string>();

            // Matrix dims
            if (ConstraintMatrix == null)
                errors.Add("Constraint matrix is null");
            else if (ConstraintMatrix.GetLength(0) != ConstraintCount || ConstraintMatrix.GetLength(1) != TotalVariables)
                errors.Add($"Constraint matrix dimensions {ConstraintMatrix.GetLength(0)}x{ConstraintMatrix.GetLength(1)} don't match {ConstraintCount}x{TotalVariables}");

            // Vector lengths
            if (ObjectiveCoefficients == null || ObjectiveCoefficients.Length != TotalVariables)
                errors.Add($"Objective coefficients length {ObjectiveCoefficients?.Length ?? 0} doesn't match total variables {TotalVariables}");

            if (RightHandSide == null || RightHandSide.Length != ConstraintCount)
                errors.Add($"RHS length {RightHandSide?.Length ?? 0} doesn't match constraint count {ConstraintCount}");

            if (LowerBounds == null || LowerBounds.Length != TotalVariables)
                errors.Add($"Lower bounds length {LowerBounds?.Length ?? 0} doesn't match total variables {TotalVariables}");

            if (UpperBounds == null || UpperBounds.Length != TotalVariables)
                errors.Add($"Upper bounds length {UpperBounds?.Length ?? 0} doesn't match total variables {TotalVariables}");

            if (VariableTypes == null || VariableTypes.Length != TotalVariables)
                errors.Add($"Variable types length {VariableTypes?.Length ?? 0} doesn't match total variables {TotalVariables}");

            // RHS non-negative and finite
            for (int i = 0; i < (RightHandSide?.Length ?? 0); i++)
            {
                if (double.IsNaN(RightHandSide[i]) || double.IsInfinity(RightHandSide[i]))
                    errors.Add($"RHS value at constraint {i} is NaN/Infinity");
                else if (RightHandSide[i] < -1e-10)
                    errors.Add($"RHS value {RightHandSide[i]:F6} at constraint {i} is negative");
            }

            // Bounds non-negative and finite
            for (int j = 0; j < (LowerBounds?.Length ?? 0); j++)
            {
                if (double.IsNaN(LowerBounds[j]) || double.IsInfinity(LowerBounds[j]))
                    errors.Add($"Lower bound for var {j} is NaN/Infinity");
                if (double.IsNaN(UpperBounds[j]) || (double.IsInfinity(UpperBounds[j]) && UpperBounds[j] < 0))
                    errors.Add($"Upper bound for var {j} is invalid");

                if (LowerBounds[j] < -1e-10)
                    errors.Add($"Lower bound {LowerBounds[j]:F6} for variable {j} is negative in canonical form");
                if (LowerBounds[j] > UpperBounds[j])
                    errors.Add($"Lower bound {LowerBounds[j]:F6} > upper bound {UpperBounds[j]:F6} for variable {j}");
            }

            // Objective vector sanity
            for (int j = 0; j < (ObjectiveCoefficients?.Length ?? 0); j++)
            {
                if (double.IsNaN(ObjectiveCoefficients[j]) || double.IsInfinity(ObjectiveCoefficients[j]))
                    errors.Add($"Objective coefficient for var {j} is NaN/Infinity");
            }

            if (errors.Count > 0)
                throw new InvalidOperationException("Canonical form validation failed:\n" + string.Join("\n", errors));
        }

        /// <summary>
        /// Creates a formatted string representation of the canonical form
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Canonical Form: {ModelName}");
            sb.AppendLine($"Type: {ObjectiveType} ({(IsIntegerProgram ? "IP" : "LP")})");
            sb.AppendLine($"Variables: {TotalVariables} (Original: {OriginalVariables})");
            sb.AppendLine($"Constraints: {ConstraintCount}");
            sb.AppendLine($"Requires Phase I: {RequiresPhaseI}");
            if (RequiresPhaseI)
                sb.AppendLine($"Artificial Variables: {ArtificialVariableIndices.Count}");
            return sb.ToString();
        }

        /// <summary>
        /// Gets detailed formulation string
        /// </summary>
        public string ToFormulationString(bool phaseI = false)
        {
            var sb = new StringBuilder();

            // Objective
            sb.AppendLine(GetObjectiveString(phaseI));

            // Constraints
            sb.AppendLine("\nSUBJECT TO:");
            for (int i = 0; i < ConstraintCount; i++)
                sb.AppendLine($"  {GetConstraintString(i, includeSlack: true)}");

            // Variable bounds
            sb.AppendLine("\nVARIABLE BOUNDS:");
            for (int j = 0; j < TotalVariables; j++)
            {
                var name = VariableMapping.GetCanonicalVariableName(j);
                var type = VariableMapping.GetCanonicalVariableType(j);

                if (UpperBounds[j] < double.PositiveInfinity)
                    sb.AppendLine($"  {LowerBounds[j]:F3} <= {name} <= {UpperBounds[j]:F3} ({type})");
                else
                    sb.AppendLine($"  {name} >= {LowerBounds[j]:F3} ({type})");
            }

            return sb.ToString();
        }

        public bool IsArtificialVariable(int canonicalIndex)
        {
            if (ArtificialVariableIndices != null && ArtificialVariableIndices.Contains(canonicalIndex))
                return true;

            if (VariableMapping?.AuxiliaryVariables != null
                && VariableMapping.AuxiliaryVariables.TryGetValue(canonicalIndex, out var aux))
                return aux.Type == AuxiliaryVariableType.Artificial;

            return false;
        }

        public bool IsSlackVariable(int canonicalIndex)
        {
            return VariableMapping?.AuxiliaryVariables != null
                && VariableMapping.AuxiliaryVariables.TryGetValue(canonicalIndex, out var aux)
                && aux.Type == AuxiliaryVariableType.Slack;
        }

        public bool IsSurplusVariable(int canonicalIndex)
        {
            return VariableMapping?.AuxiliaryVariables != null
                && VariableMapping.AuxiliaryVariables.TryGetValue(canonicalIndex, out var aux)
                && aux.Type == AuxiliaryVariableType.Surplus;
        }

    }
}