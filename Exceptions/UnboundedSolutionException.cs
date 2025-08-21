using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OptiSolver.NET.Exceptions
{
    /// <summary>
    /// Exception thrown when the objective function is unbounded
    /// </summary>
    public class UnboundedSolutionException : Exception
    {
        public UnboundedSolutionException(string message) : base(message)
        {
        }

        public UnboundedSolutionException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
