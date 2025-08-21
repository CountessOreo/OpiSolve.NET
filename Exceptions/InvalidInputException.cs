namespace OptiSolver.NET.Exceptions
{
    /// <summary>
    /// Exception thrown when input file or string format is invalid
    /// </summary>
    public class InvalidInputException : Exception
    {
        public InvalidInputException(string message) : base(message)
        {
        }

        public InvalidInputException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}