using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OptiSolver.NET.Exceptions
{
    /// <summary>
    /// Exception thrown when algorithm fails to converge or encounters a critical error
    /// </summary>
    public class AlgorithmException : Exception
    {
        public AlgorithmException(string message) : base(message)
        {
        }

        public AlgorithmException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
