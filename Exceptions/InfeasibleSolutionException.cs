using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OptiSolver.NET.Exceptions
{
    /// <summary>
    /// Exception thrown when the optimization problem has no feasible solution
    /// </summary>
    public class InfeasibleSolutionException : Exception
    {
        public InfeasibleSolutionException(string message) : base(message)
        {
        }

        public InfeasibleSolutionException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
