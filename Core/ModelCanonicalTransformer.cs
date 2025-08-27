using System;
using System.Collections.Generic;
using System.Linq;

namespace OptiSolver.NET.Core
{
    /// <summary>
    /// Converts LPModel to CanonicalForm for simplex algorithms.
    /// Handles variable splitting, auxiliary variable addition, and constraint standardization.
    /// Also populates per-column VarInfo metadata (Decision/Slack/Surplus/Artificial).
    /// </summary>
    public class ModelCanonicalTransformer
    {
        // ---- NEW: we accumulate VarInfo for every canonical column as we build them ----
        private List<VarInfo> _varInfos;   

        /// <summary>
        /// Converts an LPModel to canonical form.
        /// </summary>
        public CanonicalForm Canonicalize(LPModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            // Validate the original model first
            model.ValidateModel();

            _varInfos = new List<VarInfo>(); 

            var canonical = new CanonicalForm
            {
                ModelName = model.Name,
                ObjectiveType = model.ObjectiveType,
                OriginalVariables = model.VariableCount,
                ConstraintCount = model.ConstraintCount
            };

            // 1) Variable mapping (URS split, Negative substitution, etc.)
            var mapping = CreateVariableMapping(model, canonicalVarInfoSink: _varInfos);
            canonical.VariableMapping = mapping;
            canonical.TotalVariables = mapping.TotalCanonicalVariables;

            // 2) Process constraints so that RHS >= 0 (flip row if needed, flip relation accordingly)
            var processed = ProcessConstraintsToNonNegativeRhs(model);

            // 3) Add auxiliary variables per row (Slack for <= ; Surplus + Artificial for >= ; Artificial for =)
            AddAuxiliaries(canonical, processed, mapping, canonicalVarInfoSink: _varInfos);

            // 4) Build A matrix and b
            BuildConstraintMatrix(canonical, processed, mapping);

            // 5) Build objective vector
            BuildObjectiveFunction(canonical, model, mapping);

            // 6) Bounds & types
            SetVariableBoundsAndTypes(canonical, model, mapping);

            // 7) Phase I flag
            SetupPhaseI(canonical);

            // ---- NEW: attach column metadata to CanonicalForm so solvers (and B&B) can query it ----
            // If your CanonicalForm uses a different property name, change "Columns" below.
            canonical.Columns = _varInfos;

            // Final validation
            canonical.Validate();
            return canonical;
        }

        // ------------------- helpers -------------------

        private VariableMapping CreateVariableMapping(LPModel model, List<VarInfo> canonicalVarInfoSink)
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

                    // NEW: add VarInfo for x+ and x- (both map back to original i; they are still "Decision" columns)
                    canonicalVarInfoSink.Add(new VarInfo { Kind = VarKind.Decision, OriginalIndex = i, Name = $"{v.Name}_plus" });   // col canonicalIndex
                    canonicalVarInfoSink.Add(new VarInfo { Kind = VarKind.Decision, OriginalIndex = i, Name = $"{v.Name}_minus" });   // col canonicalIndex+1

                    canonicalIndex += 2;
                    break;

                    case VariableType.Negative:
                    // x <= 0  ->  x = -y, y >= 0
                    mapping.AddNegativeVariableMapping(i, v.Name, canonicalIndex);

                    // NEW: y is still a "Decision" column representing original i
                    canonicalVarInfoSink.Add(new VarInfo { Kind = VarKind.Decision, OriginalIndex = i, Name = $"{v.Name}_negsub" });

                    canonicalIndex += 1;
                    break;

                    default:
                    // Positive / Continuous / Integer / Binary (>= 0)
                    mapping.AddRegularVariableMapping(i, v.Name, canonicalIndex);

                    // NEW: regular canonical decision column mapped to original i
                    canonicalVarInfoSink.Add(new VarInfo { Kind = VarKind.Decision, OriginalIndex = i, Name = v.Name });

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
        /// Also appends VarInfo for each auxiliary column.
        /// </summary>
        private void AddAuxiliaries(CanonicalForm canonical, List<ProcessedConstraint> rows, VariableMapping mapping, List<VarInfo> canonicalVarInfoSink)
        {
            int next = canonical.TotalVariables;

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];

                if (r.Relation == ConstraintRelation.LessThanOrEqual)
                {
                    // +s_i
                    mapping.AddAuxiliaryVariable(next, AuxiliaryVariableType.Slack, i, $"s{i + 1}");
                    canonicalVarInfoSink.Add(new VarInfo { Kind = VarKind.Slack, OriginalIndex = null, Name = $"s{i + 1}" });
                    next++;
                }
                else if (r.Relation == ConstraintRelation.GreaterThanOrEqual)
                {
                    // -e_i + a_i
                    mapping.AddAuxiliaryVariable(next, AuxiliaryVariableType.Surplus, i, $"e{i + 1}");
                    canonicalVarInfoSink.Add(new VarInfo { Kind = VarKind.Surplus, OriginalIndex = null, Name = $"e{i + 1}" });
                    next++;

                    mapping.AddAuxiliaryVariable(next, AuxiliaryVariableType.Artificial, i, $"a{i + 1}");
                    canonical.ArtificialVariableIndices.Add(next);
                    canonical.RequiresPhaseI = true;
                    canonicalVarInfoSink.Add(new VarInfo { Kind = VarKind.Artificial, OriginalIndex = null, Name = $"a{i + 1}" });
                    next++;
                }
                else // Equal
                {
                    // +a_i
                    mapping.AddAuxiliaryVariable(next, AuxiliaryVariableType.Artificial, i, $"a{i + 1}");
                    canonical.ArtificialVariableIndices.Add(next);
                    canonical.RequiresPhaseI = true;
                    canonicalVarInfoSink.Add(new VarInfo { Kind = VarKind.Artificial, OriginalIndex = null, Name = $"a{i + 1}" });
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

                int idx;

                // Slack (+1)
                idx = GetAuxIndex(mapping, AuxiliaryVariableType.Slack, i);
                if (idx >= 0)
                    canonical.ConstraintMatrix[i, idx] = +1.0;

                // Surplus (-1)
                idx = GetAuxIndex(mapping, AuxiliaryVariableType.Surplus, i);
                if (idx >= 0)
                    canonical.ConstraintMatrix[i, idx] = -1.0;

                // Artificial (+1)
                idx = GetAuxIndex(mapping, AuxiliaryVariableType.Artificial, i);
                if (idx >= 0)
                    canonical.ConstraintMatrix[i, idx] = +1.0;

                canonical.RightHandSide[i] = r.RHS;
            }
        }

        private static int GetAuxIndex(VariableMapping mapping, AuxiliaryVariableType type, int constraintRow)
        {
            foreach (var kv in mapping.AuxiliaryVariables)
            {
                if (kv.Value.Type == type && kv.Value.ConstraintIndex == constraintRow)
                    return kv.Key;
            }
            return -1;
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
                    // Defensive defaults for structural variable with no mapping (should be rare)
                    canonical.VariableTypes[j] = VariableType.Positive;
                    canonical.LowerBounds[j] = 0.0;
                    canonical.UpperBounds[j] = double.PositiveInfinity;
                    continue; // <-- add continue so we don't fall through to use 'info'
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
                canonical.VariableTypes[j] = VariableType.Positive;
            }
        }

        private void SetupPhaseI(CanonicalForm canonical)
        {
            canonical.RequiresPhaseI = canonical.ArtificialVariableIndices.Count > 0;
        }
    }
}
