using System;

namespace OptiSolver.NET.Services.BranchAndBound
{
    /// <summary>
    /// Node state for Branch & Bound over 0-1 Knapsack.
    /// </summary>
    internal sealed class KnapsackNode
    {
        public int NodeId { get; set; }
        public int Depth { get; set; }          // how many decisions fixed
        public int NextItem { get; set; }       // index of next item to consider (in sorted order)
        public double Value { get; set; }       // current total value
        public double Weight { get; set; }      // current total weight
        public double Bound { get; set; }       // fractional knapsack upper bound
        public bool[] Take { get; }             // decision vector (true/false)

        public KnapsackNode(int n)
        {
            Take = new bool[n];
        }

        public KnapsackNode CloneShallow()
        {
            var k = new KnapsackNode(Take.Length)
            {
                NodeId = NodeId,
                Depth = Depth,
                NextItem = NextItem,
                Value = Value,
                Weight = Weight,
                Bound = Bound
            };
            Array.Copy(Take, k.Take, Take.Length);
            return k;
        }
    }
}
