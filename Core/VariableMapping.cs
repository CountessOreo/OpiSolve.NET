using System;
using System.Collections.Generic;
using System.Linq;

namespace OptiSolver.NET.Core
{
    /// <summary>
    /// Maps between original model variables and canonical form variables
    /// Essential for translating solutions back to original variable names
    /// and for expanding rows into canonical space.
    /// </summary>
    public class VariableMapping
    {
        /// <summary>
        /// Backward-compatible: original var index -> list of canonical variable indices.
        /// (Do not use this for arithmetic; use the signed map.)
        /// </summary>
        public Dictionary<int, List<int>> OriginalToCanonical { get; private set; }

        /// <summary>
        /// Signed map: original var index -> list of (canonical index, sign multiplier).
        /// For unrestricted x: [(x+, +1), (x-, -1)]
        /// For negative x ≤ 0: [(y, -1)] where x = -y, y ≥ 0
        /// For regular: [(z, +1)]
        /// </summary>
        public Dictionary<int, List<(int index, double sign)>> OriginalToCanonicalSigned { get; private set; }

        /// <summary>
        /// Maps canonical variable index back to original variable info
        /// </summary>
        public Dictionary<int, OriginalVariableInfo> CanonicalToOriginal { get; private set; }

        /// <summary>
        /// Information about auxiliary variables (slack, surplus, artificial)
        /// </summary>
        public Dictionary<int, AuxiliaryVariableInfo> AuxiliaryVariables { get; private set; }

        /// <summary>
        /// Total number of variables in canonical form
        /// </summary>
        public int TotalCanonicalVariables { get; private set; }

        /// <summary>
        /// Number of original decision variables
        /// </summary>
        public int OriginalVariableCount { get; private set; }

        public VariableMapping(int originalVariableCount)
        {
            OriginalVariableCount = originalVariableCount;
            OriginalToCanonical = new Dictionary<int, List<int>>();
            OriginalToCanonicalSigned = new Dictionary<int, List<(int index, double sign)>>();
            CanonicalToOriginal = new Dictionary<int, OriginalVariableInfo>();
            AuxiliaryVariables = new Dictionary<int, AuxiliaryVariableInfo>();
            TotalCanonicalVariables = 0;
        }

        /// <summary>
        /// Adds mapping for a regular variable (1:1 mapping, default +1 sign)
        /// </summary>
        public void AddRegularVariableMapping(int originalIndex, string originalName, int canonicalIndex)
        {
            AddRegularVariableMapping(originalIndex, originalName, canonicalIndex, +1.0,
                CanonicalVariableType.Regular, VariableSplitComponent.None);
        }

        /// <summary>
        /// Adds mapping for a regular (possibly sign-flipped) variable with metadata.
        /// Use sign = -1 for negative-substitution x = -y.
        /// </summary>
        public void AddRegularVariableMapping(
            int originalIndex,
            string originalName,
            int canonicalIndex,
            double sign,
            CanonicalVariableType varType,
            VariableSplitComponent component)
        {
            if (!OriginalToCanonical.ContainsKey(originalIndex))
                OriginalToCanonical[originalIndex] = new List<int>();
            if (!OriginalToCanonicalSigned.ContainsKey(originalIndex))
                OriginalToCanonicalSigned[originalIndex] = new List<(int, double)>();

            OriginalToCanonical[originalIndex].Add(canonicalIndex);
            OriginalToCanonicalSigned[originalIndex].Add((canonicalIndex, sign));

            CanonicalToOriginal[canonicalIndex] = new OriginalVariableInfo
            {
                OriginalIndex = originalIndex,
                OriginalName = originalName,
                VariableType = varType,
                SplitComponent = component
            };

            TotalCanonicalVariables = Math.Max(TotalCanonicalVariables, canonicalIndex + 1);
        }

        /// <summary>
        /// Adds mapping for unrestricted variable split into x+ and x-
        /// </summary>
        public void AddUnrestrictedVariableMapping(int originalIndex, string originalName,
                                                   int canonicalPlusIndex, int canonicalMinusIndex)
        {
            OriginalToCanonical[originalIndex] = new List<int> { canonicalPlusIndex, canonicalMinusIndex };
            OriginalToCanonicalSigned[originalIndex] = new List<(int, double)>
            {
                (canonicalPlusIndex, +1.0),
                (canonicalMinusIndex, -1.0)
            };

            CanonicalToOriginal[canonicalPlusIndex] = new OriginalVariableInfo
            {
                OriginalIndex = originalIndex,
                OriginalName = $"{originalName}+",
                VariableType = CanonicalVariableType.UnrestrictedPositive,
                SplitComponent = VariableSplitComponent.Positive
            };

            CanonicalToOriginal[canonicalMinusIndex] = new OriginalVariableInfo
            {
                OriginalIndex = originalIndex,
                OriginalName = $"{originalName}-",
                VariableType = CanonicalVariableType.UnrestrictedNegative,
                SplitComponent = VariableSplitComponent.Negative
            };

            TotalCanonicalVariables = Math.Max(TotalCanonicalVariables,
                                               Math.Max(canonicalPlusIndex, canonicalMinusIndex) + 1);
        }

        /// <summary>
        /// Adds mapping for a negative variable (x ≤ 0) using substitution x = -y, y ≥ 0.
        /// </summary>
        public void AddNegativeVariableMapping(int originalIndex, string originalName, int canonicalIndex)
        {
            AddRegularVariableMapping(originalIndex, $"{originalName}_neg", canonicalIndex, -1.0,
                CanonicalVariableType.NegativeSubstitution, VariableSplitComponent.None);
        }

        /// <summary>
        /// Adds auxiliary variable (slack, surplus, artificial)
        /// </summary>
        public void AddAuxiliaryVariable(int canonicalIndex, AuxiliaryVariableType auxType,
                                         int constraintIndex, string name = null)
        {
            AuxiliaryVariables[canonicalIndex] = new AuxiliaryVariableInfo
            {
                Type = auxType,
                ConstraintIndex = constraintIndex,
                Name = name ?? GenerateAuxiliaryName(auxType, constraintIndex)
            };

            TotalCanonicalVariables = Math.Max(TotalCanonicalVariables, canonicalIndex + 1);
        }

        /// <summary>
        /// Gets the original variable value from canonical form solution (uses signed mapping).
        /// </summary>
        public double GetOriginalVariableValue(int originalIndex, double[] canonicalSolution)
        {
            if (!OriginalToCanonicalSigned.ContainsKey(originalIndex))
                throw new ArgumentException($"Original variable {originalIndex} not found in mapping");

            double val = 0.0;
            foreach (var (idx, sign) in OriginalToCanonicalSigned[originalIndex])
            {
                if (idx < 0 || idx >= canonicalSolution.Length)
                    throw new ArgumentOutOfRangeException(nameof(canonicalSolution),
                        $"Canonical solution length {canonicalSolution.Length} is less than required index {idx + 1}");
                val += sign * canonicalSolution[idx];
            }
            return val;
        }

        /// <summary>
        /// Gets all original variable values from canonical solution
        /// </summary>
        public double[] GetOriginalSolution(double[] canonicalSolution)
        {
            var originalSolution = new double[OriginalVariableCount];
            for (int i = 0; i < OriginalVariableCount; i++)
                originalSolution[i] = GetOriginalVariableValue(i, canonicalSolution);
            return originalSolution;
        }

        /// <summary>
        /// Expands an original coefficient row (for constraints or objective) into canonical space:
        /// c_canonical[j] = sum_i ( sign(i→j) * c_original[i] )
        /// </summary>
        public double[] ExpandOriginalRowToCanonical(IReadOnlyList<double> originalCoeffs, int? totalCanonicalOverride = null)
        {
            int nCanon = totalCanonicalOverride ?? TotalCanonicalVariables;
            var row = new double[nCanon];

            if (originalCoeffs == null || originalCoeffs.Count != OriginalVariableCount)
                throw new ArgumentException($"Expected {OriginalVariableCount} original coefficients, got {originalCoeffs?.Count ?? 0}");

            for (int i = 0; i < OriginalVariableCount; i++)
            {
                if (!OriginalToCanonicalSigned.TryGetValue(i, out var maps))
                    throw new InvalidOperationException($"No mapping found for original variable index {i}");

                foreach (var (j, sign) in maps)
                {
                    if (j >= nCanon)
                        throw new InvalidOperationException($"Canonical index {j} exceeds total canonical variables {nCanon}");
                    row[j] += sign * originalCoeffs[i];
                }
            }

            return row;
        }

        /// <summary>
        /// Gets name of canonical variable for display
        /// </summary>
        public string GetCanonicalVariableName(int canonicalIndex)
        {
            if (CanonicalToOriginal.ContainsKey(canonicalIndex))
                return CanonicalToOriginal[canonicalIndex].OriginalName;

            if (AuxiliaryVariables.ContainsKey(canonicalIndex))
                return AuxiliaryVariables[canonicalIndex].Name;

            return $"x{canonicalIndex + 1}";
        }

        /// <summary>
        /// Gets type of canonical variable
        /// </summary>
        public string GetCanonicalVariableType(int canonicalIndex)
        {
            if (CanonicalToOriginal.ContainsKey(canonicalIndex))
                return CanonicalToOriginal[canonicalIndex].VariableType.ToString();

            if (AuxiliaryVariables.ContainsKey(canonicalIndex))
                return AuxiliaryVariables[canonicalIndex].Type.ToString();

            return "Unknown";
        }

        /// <summary>
        /// Checks if a canonical variable is an auxiliary variable
        /// </summary>
        public bool IsAuxiliaryVariable(int canonicalIndex) => AuxiliaryVariables.ContainsKey(canonicalIndex);

        /// <summary>
        /// Gets all auxiliary variable indices (optionally filtered by type)
        /// </summary>
        public List<int> GetAuxiliaryVariableIndices(AuxiliaryVariableType? type = null)
        {
            if (type.HasValue)
                return AuxiliaryVariables.Where(kv => kv.Value.Type == type.Value).Select(kv => kv.Key).ToList();

            return AuxiliaryVariables.Keys.ToList();
        }

        /// <summary>
        /// Generates standard name for auxiliary variables
        /// </summary>
        private string GenerateAuxiliaryName(AuxiliaryVariableType type, int constraintIndex)
        {
            return type switch
            {
                AuxiliaryVariableType.Slack => $"s{constraintIndex + 1}",
                AuxiliaryVariableType.Surplus => $"e{constraintIndex + 1}",
                AuxiliaryVariableType.Artificial => $"a{constraintIndex + 1}",
                _ => $"aux{constraintIndex + 1}"
            };
        }

        /// <summary>
        /// Creates a summary string of the variable mapping
        /// </summary>
        public string GetMappingSummary()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Variable Mapping Summary:");
            sb.AppendLine($"Original variables: {OriginalVariableCount}");
            sb.AppendLine($"Total canonical variables: {TotalCanonicalVariables}");
            sb.AppendLine($"Auxiliary variables: {AuxiliaryVariables.Count}");

            sb.AppendLine("\nOriginal to Canonical Mapping (with signs):");
            foreach (var kvp in OriginalToCanonicalSigned.OrderBy(k => k.Key))
            {
                var parts = kvp.Value.Select(p => $"{GetCanonicalVariableName(p.index)}(idx {p.index}, sign {(p.sign >= 0 ? "+" : "")}{p.sign:F0})");
                sb.AppendLine($"  x{kvp.Key + 1} → [ {string.Join(", ", parts)} ]");
            }

            sb.AppendLine("\nAuxiliary Variables:");
            foreach (var kvp in AuxiliaryVariables.OrderBy(k => k.Key))
            {
                var aux = kvp.Value;
                sb.AppendLine($"  {aux.Name} (index {kvp.Key}) - {aux.Type} for constraint {aux.ConstraintIndex + 1}");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Information about original variables in canonical form
    /// </summary>
    public class OriginalVariableInfo
    {
        public int OriginalIndex { get; set; }
        public string OriginalName { get; set; }
        public CanonicalVariableType VariableType { get; set; }
        public VariableSplitComponent SplitComponent { get; set; }
    }

    /// <summary>
    /// Information about auxiliary variables
    /// </summary>
    public class AuxiliaryVariableInfo
    {
        public AuxiliaryVariableType Type { get; set; }
        public int ConstraintIndex { get; set; }
        public string Name { get; set; }
    }
}
