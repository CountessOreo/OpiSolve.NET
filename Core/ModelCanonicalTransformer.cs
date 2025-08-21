using System;
using System.Collections.Generic;
using System.Linq;

namespace OptiSolver.NET.Core
{
    /// <summary>
    /// Converts LPModel to CanonicalForm for simplex algorithms.
    /// Handles variable splitting, auxiliary variable addition, and constraint standardization.
    /// </summary>
    public class ModelCanonicalTransformer
    {
        /// <summary>
        /// Converts an LPModel to canonical form.
        /// </summary>
        public CanonicalForm Canonicalize(LPModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            // Validate the original model first
            model.ValidateModel();

            var canonical = new CanonicalForm
            {
                ModelName = model.Name,
                ObjectiveType = model.ObjectiveType,
                OriginalVariables = model.VariableCount,
                ConstraintCount = model.ConstraintCount
            };

            // 1) Variable mapping (URS split, Negative substitution, etc.)
            var mapping = CreateVariableMapping(model);
            canonical.VariableMapping = mapping;
            canonical.TotalVariables = mapping.TotalCanonicalVariables;

            // 2) Process constraints so that RHS >= 0 (flip row if needed, flip relation accordingly)
            var processed = ProcessConstraintsToNonNegativeRhs(model);

            // 3) Add auxiliary variables per row (Slack for <= ; Surplus + Artificial for >= ; Artificial for =)
            AddAuxiliaries(canonical, processed, mapping);

            // 4) Build A matrix and b
            BuildConstraintMatrix(canonical, processed, mapping);

            // 5) Build objective vector
            BuildObjectiveFunction(canonical, model, mapping);

            // 6) Bounds & types
            SetVariableBoundsAndTypes(canonical, model, mapping);

            // 7) Phase I flag
            SetupPhaseI(canonical);

            // Final validation
            canonical.Validate();
            return canonical;
        }

        private VariableMapping CreateVariableMapping(LPModel model)
        {
            var mapping = new VariableMapping(model.VariableCount);
            int canonicalIndex = 0;

            for (int i = 0; i < model.VariableCount; i++)
            {
                var v = model.Variables[i];

                switch (v.Type)
                {
                    case VariableType.Unrestricted:
                    // x = x+ - x-, both >= 0
                    mapping.AddUnrestrictedVariableMapping(i, v.Name, canonicalIndex, canonicalIndex + 1);
                    canonicalIndex += 2;
                    break;

                    case VariableType.Negative:
                    // x <= 0  ->  x = -y, y >= 0
                    mapping.AddNegativeVariableMapping(i, v.Name, canonicalIndex);
                    canonicalIndex += 1;
                    break;

                    default:
                    // Positive / Continuous / Integer / Binary (>= 0)
                    mapping.AddRegularVariableMapping(i, v.Name, canonicalIndex);
                    canonicalIndex += 1;
                    break;
                }
            }

            return mapping;
        }

        private sealed class ProcessedConstraint
        {
            public int OriginalIndex { get; set; }
            public List<double> Coefficients { get; set; }
            public ConstraintRelation Relation { get; set; }
            public double RHS { get; set; }
        }

        /// <summary>
        /// Ensures each constraint has RHS >= 0 by multiplying entire row by -1 if needed.
        /// Relation is flipped if the row is negated (<= <-> >=; '=' unchanged).
        /// </summary>
        private List<ProcessedConstraint> ProcessConstraintsToNonNegativeRhs(LPModel model)
        {
            var list = new List<ProcessedConstraint>();

            foreach (var c in model.Constraints)
            {
                var pc = new ProcessedConstraint
                {
                    OriginalIndex = c.Index,
                    Coefficients = new List<double>(c.Coefficients),
                    Relation = c.Relation,
                    RHS = c.RightHandSide
                };

                if (pc.RHS < 0)
                {
                    // Multiply by -1
                    for (int i = 0; i < pc.Coefficients.Count; i++)
                        pc.Coefficients[i] = -pc.Coefficients[i];

                    pc.RHS = -pc.RHS;

                    // Flip relation
                    pc.Relation = pc.Relation switch
                    {
                        ConstraintRelation.LessThanOrEqual => ConstraintRelation.GreaterThanOrEqual,
                        ConstraintRelation.GreaterThanOrEqual => ConstraintRelation.LessThanOrEqual,
                        ConstraintRelation.Equal => ConstraintRelation.Equal,
                        _ => pc.Relation
                    };
                }

                list.Add(pc);
            }

            return list;
        }

        /// <summary>
        /// Adds Slack / Surplus / Artificial variables by row and updates TotalVariables and mapping.
        /// </summary>
        private void AddAuxiliaries(CanonicalForm canonical, List<ProcessedConstraint> rows, VariableMapping mapping)
        {
            int next = canonical.TotalVariables;

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];

                if (r.Relation == ConstraintRelation.LessThanOrEqual)
                {
                    // +s_i
                    mapping.AddAuxiliaryVariable(next, AuxiliaryVariableType.Slack, i, $"s{i + 1}");
                    next++;
                }
                else if (r.Relation == ConstraintRelation.GreaterThanOrEqual)
                {
                    // -e_i + a_i
                    mapping.AddAuxiliaryVariable(next, AuxiliaryVariableType.Surplus, i, $"e{i + 1}");
                    next++;
                    mapping.AddAuxiliaryVariable(next, AuxiliaryVariableType.Artificial, i, $"a{i + 1}");
                    canonical.ArtificialVariableIndices.Add(next);
                    canonical.RequiresPhaseI = true;
                    next++;
                }
                else // Equal
                {
                    // +a_i
                    mapping.AddAuxiliaryVariable(next, AuxiliaryVariableType.Artificial, i, $"a{i + 1}");
                    canonical.ArtificialVariableIndices.Add(next);
                    canonical.RequiresPhaseI = true;
                    next++;
                }
            }

            canonical.TotalVariables = next;
        }

        private void BuildConstraintMatrix(CanonicalForm canonical, List<ProcessedConstraint> rows, VariableMapping mapping)
        {
            canonical.ConstraintMatrix = new double[canonical.ConstraintCount, canonical.TotalVariables];
            canonical.RightHandSide = new double[canonical.ConstraintCount];

            int structuralCols = mapping.TotalCanonicalVariables;

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];

                // Expand original coefficients into canonical structural space
                var expanded = mapping.ExpandOriginalRowToCanonical(r.Coefficients, structuralCols);
                for (int j = 0; j < structuralCols; j++)
                    canonical.ConstraintMatrix[i, j] = expanded[j];

                // Auxiliary columns
                // find indices by (constraintIndex, type)
                // Slack (+1), Surplus (-1), Artificial (+1)
                var slackIdx = mapping.AuxiliaryVariables
                    .FirstOrDefault(kv => kv.Value.Type == AuxiliaryVariableType.Slack && kv.Value.ConstraintIndex == i).Key;
                if (mapping.AuxiliaryVariables.ContainsKey(slackIdx))
                    canonical.ConstraintMatrix[i, slackIdx] = +1.0;

                var surplusIdx = mapping.AuxiliaryVariables
                    .FirstOrDefault(kv => kv.Value.Type == AuxiliaryVariableType.Surplus && kv.Value.ConstraintIndex == i).Key;
                if (mapping.AuxiliaryVariables.ContainsKey(surplusIdx))
                    canonical.ConstraintMatrix[i, surplusIdx] = -1.0;

                var artificialIdx = mapping.AuxiliaryVariables
                    .FirstOrDefault(kv => kv.Value.Type == AuxiliaryVariableType.Artificial && kv.Value.ConstraintIndex == i).Key;
                if (mapping.AuxiliaryVariables.ContainsKey(artificialIdx))
                    canonical.ConstraintMatrix[i, artificialIdx] = +1.0;

                canonical.RightHandSide[i] = r.RHS;
            }
        }

        private void BuildObjectiveFunction(CanonicalForm canonical, LPModel model, VariableMapping mapping)
        {
            int n = canonical.TotalVariables;
            int structuralCols = mapping.TotalCanonicalVariables;

            canonical.ObjectiveCoefficients = new double[n];

            // Expand original objective across canonical structural variables
            var originalObj = model.Variables.Select(v => v.Coefficient).ToList();
            var expandedObj = mapping.ExpandOriginalRowToCanonical(originalObj, structuralCols);

            for (int j = 0; j < structuralCols; j++)
                canonical.ObjectiveCoefficients[j] = expandedObj[j];

            // Aux variables have 0 objective coefficient in the original problem
            for (int j = structuralCols; j < n; j++)
                canonical.ObjectiveCoefficients[j] = 0.0;
        }

        private void SetVariableBoundsAndTypes(CanonicalForm canonical, LPModel model, VariableMapping mapping)
        {
            int n = canonical.TotalVariables;
            int structuralCols = mapping.TotalCanonicalVariables;

            canonical.LowerBounds = new double[n];
            canonical.UpperBounds = new double[n];
            canonical.VariableTypes = new VariableType[n];

            // Structural columns: derive from original types
            for (int j = 0; j < structuralCols; j++)
            {
                if (!mapping.CanonicalToOriginal.TryGetValue(j, out var info))
                {
                    // Defensive defaults
                    canonical.LowerBounds[j] = 0.0;
                    canonical.UpperBounds[j] = double.PositiveInfinity;
                    continue;
                }

                var origIdx = info.OriginalIndex;
                var origType = model.Variables[origIdx].Type;

                // Integer/Binary must be preserved; otherwise continuous nonnegative
                var canonType = origType switch
                {
                    VariableType.Binary => VariableType.Binary,
                    VariableType.Integer => VariableType.Integer,
                    _ => VariableType.Positive // default for continuous (>=0 in canonical)
                };

                canonical.VariableTypes[j] = canonType;

                var (lo, hi) = EnumHelper.GetDefaultBounds(canonType);
                canonical.LowerBounds[j] = Math.Max(0.0, lo); // canonical is >= 0
                canonical.UpperBounds[j] = double.IsPositiveInfinity(hi) ? double.PositiveInfinity : Math.Max(0.0, hi);
            }

            // Auxiliary columns: nonnegative continuous
            for (int j = structuralCols; j < n; j++)
            {
                canonical.LowerBounds[j] = 0.0;
                canonical.UpperBounds[j] = double.PositiveInfinity;
            }
        }

        private void SetupPhaseI(CanonicalForm canonical)
        {
            canonical.RequiresPhaseI = canonical.ArtificialVariableIndices.Count > 0;
        }
    }
}
