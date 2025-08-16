using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OptiSolver.NET.Services.BranchAndBound
{
    /// <summary>
    /// Represents an item in the knapsack problem
    /// </summary>
    public class KnapsackItem
    {
        public int Index { get; set; }
        public double Value { get; set; }
        public double Weight { get; set; }
    }
}
