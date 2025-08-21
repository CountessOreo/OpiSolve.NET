using System;

namespace OptiSolver.NET.Services.BranchAndBound
{
    /// <summary>
    /// Simple value/weight item for 0-1 knapsack.
    /// </summary>
    public sealed class KnapsackItem
    {
        public int Index { get; }
        public double Value { get; }
        public double Weight { get; }

        public KnapsackItem(int index, double value, double weight)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (weight < 0)
                throw new ArgumentOutOfRangeException(nameof(weight));
            Index = index;
            Value = value;
            Weight = weight;
        }

        public double Ratio => Weight <= 0 ? double.PositiveInfinity : Value / Weight;
        public override string ToString() => $"#{Index} v={Value} w={Weight} r={Ratio:F3}";
    }
}
