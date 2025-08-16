using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OptiSolver.NET.Services.BranchAndBound_Knapsack
{
    /// <summary>
    /// Represents a node in the branch and bound tree
    /// </summary>
    public class KnapsackNode
    {
        public int Id { get; set; }
        public int Level { get; set; } // Which item we're deciding on (-1 for root)
        public double Value { get; set; } // Current objective value
        public double Weight { get; set; } // Current weight used
        public double UpperBound { get; set; } // Upper bound estimate
        public bool[] Solution { get; set; } // Current partial solution
        public bool IsComplete { get; set; } // Whether all items have been decided

        public override string ToString()
        {
            return $"Node {Id}: Level={Level}, Value={Value:F3}, Weight={Weight:F3}, UB={UpperBound:F3}";
        }
    }
}
