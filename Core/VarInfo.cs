namespace OptiSolver.NET.Core
{
    /// <summary>
    /// Metadata for each canonical variable column.
    /// Lets solvers know if a column is an original decision var, slack, surplus, or artificial,
    /// and (if it is a decision variable) which original index it came from.
    /// </summary>
    public sealed class VarInfo
    {
        /// <summary>
        /// What kind of column this is (Decision, Slack, Surplus, Artificial).
        /// </summary>
        public VarKind Kind { get; set; }

        /// <summary>
        /// Index in the original model’s Variables list (if Kind==Decision).
        /// Null for auxiliary variables.
        /// </summary>
        public int? OriginalIndex { get; set; }

        /// <summary>
        /// Human-readable name (“x1”, “s1”, “a2”, …).
        /// </summary>
        public string Name { get; set; }
    }

    /// <summary>
    /// Categories of canonical columns.
    /// </summary>
    public enum VarKind
    {
        Decision,
        Slack,
        Surplus,
        Artificial
    }
}
