namespace OptiSolver.NET.Exceptions
{
    /// <summary>
    /// Exception thrown when input file parsing or validation fails
    /// </summary>
    public class InvalidInputException : Exception
    {
        // Line number where the error occurred
        public int? LineNumber { get; }

        // Column position where the error occurred 
        public int? ColumnPosition { get; }

        // The invalid input that caused the exception
        public string InvalidInput { get; }

        // Default constructor
        public InvalidInputException() : base("Invalid input format")
        {
        }

        /// <summary>
        /// Constructor with message
        /// </summary>
        /// <param name="message">Error message</param>
        public InvalidInputException(string message) : base(message)
        {
        }

        /// <summary>
        /// Constructor with message and inner exception
        /// </summary>
        /// <param name="message">Error message</param>
        /// <param name="innerException">Inner exception</param>
        public InvalidInputException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// Constructor with detailed location information
        /// </summary>
        /// <param name="message">Error message</param>
        /// <param name="lineNumber">Line number where error occurred</param>
        /// <param name="columnPosition">Column position where error occurred</param>
        /// <param name="invalidInput">The invalid input string</param>
        public InvalidInputException(string message, int lineNumber, int columnPosition = -1, string invalidInput = null)
            : base(message)
        {
            LineNumber = lineNumber;
            ColumnPosition = columnPosition >= 0 ? columnPosition : null;
            InvalidInput = invalidInput;
        }

        /// <summary>
        /// Gets a detailed error message including location information
        /// </summary>
        public override string Message
        {
            get
            {
                var message = base.Message;

                if (LineNumber.HasValue)
                {
                    message += $" (Line {LineNumber.Value}";

                    if (ColumnPosition.HasValue)
                    {
                        message += $", Column {ColumnPosition.Value}";
                    }

                    message += ")";
                }

                if (!string.IsNullOrEmpty(InvalidInput))
                {
                    message += $" - Invalid input: '{InvalidInput}'";
                }

                return message;
            }
        }

        /// <summary>
        /// String representation with full details
        /// </summary>
        public override string ToString()
        {
            var result = $"{GetType().Name}: {Message}";

            if (InnerException != null)
            {
                result += $"\n---> {InnerException}";
            }

            return result;
        }
    }
}