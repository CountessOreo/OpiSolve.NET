using System.Text.RegularExpressions;
using OptiSolver.NET.Core;
using OptiSolver.NET.Exceptions;

namespace OptiSolver.NET.IO
{
    /// <summary>
    /// Parses input files containing LP/IP models in the specified format
    /// Format:
    /// Line 1: max/min sign1 coeff1 sign2 coeff2 ... (objective function)
    /// Line 2-n: sign1 coeff1 sign2 coeff2 ... <=/>=/= rhs (constraints)
    /// Last line: +/-/urs/int/bin +/-/urs/int/bin ... (variable types)
    /// </summary>
    public class InputParser
    {
        private const string NumberPattern = @"[+\-]?\d+(?:\.\d+)?";
        private const string RelationPattern = @"<=|>=|=";

        /// <summary>
        /// Parses an input file and creates an LPModel
        /// </summary>
        /// <param name="filePath">Path to the input file</param>
        /// <returns>Parsed LPModel</returns>
        public LPModel ParseFile(string filePath)
        {
            // file validation checks
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Input file not found: {filePath}");

            // read file
            try
            {
                var lines = File.ReadAllLines(filePath)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => line.Trim())
                    .ToArray();

                // require at least objective + 1 constraint + types 
                if (lines.Length < 3)
                    throw new InvalidInputException("Input file must contain at least an objective function and variable types");

                return ParseLines(lines, Path.GetFileNameWithoutExtension(filePath));
            }
            catch (InvalidInputException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidInputException($"Error reading file '{filePath}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Parses input from string array
        /// </summary>
        /// <param name="lines">Lines of input</param>
        /// <param name="modelName">Name for the model</param>
        /// <returns>Parsed LPModel</returns>
        public LPModel ParseLines(string[] lines, string modelName = "Parsed Model")
        {
            if (lines == null || lines.Length == 0)
                throw new ArgumentException("Input lines cannot be null or empty", nameof(lines));

            if (lines.Length < 3)
                throw new InvalidInputException("Input must include at least one constraint line between the objective and the variable types line.");

            try
            {
                // Parse objective function (first line)
                var (objectiveType, objectiveCoefficients) = ParseObjectiveFunction(lines[0]);
                int variableCount = objectiveCoefficients.Count;

                // Create the model
                var model = new LPModel(modelName, objectiveType);

                // Parse variable types (last line)
                var variableTypes = ParseVariableTypes(lines[lines.Length - 1], variableCount);

                // Create variables
                for (int i = 0; i < variableCount; i++)
                {
                    model.AddVariable(objectiveCoefficients[i], variableTypes[i]);
                }

                // Parse constraints (middle lines)
                for (int i = 1; i < lines.Length - 1; i++)
                {
                    var constraint = ParseConstraint(lines[i], variableCount, i - 1);
                    model.AddConstraint(constraint.coefficients, constraint.relation, constraint.rhs);
                }

                // Validate the completed model
                model.ValidateModel();

                return model;
            }
            catch (InvalidInputException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidInputException($"Error parsing model: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Parses the objective function line
        /// Format: max/min sign1 coeff1 sign2 coeff2 ...
        /// Example: "max +2 +3 +3" means max z = 2x1 + 3x2 + 3x3
        /// </summary>
        private (ObjectiveType type, List<double> coefficients) ParseObjectiveFunction(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                throw new InvalidInputException("Objective function line cannot be empty");

            line = NormaliseSignNumberSpacing(line);

            var tokens = SplitLineIntoTokens(line);
            if (tokens.Count < 2)
                throw new InvalidInputException("Objective function must have at least objective type and one coefficient");

            // Parse objective type (first token)
            ObjectiveType objectiveType;
            try
            {
                objectiveType = EnumHelper.ParseObjectiveType(tokens[0]);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidInputException($"Invalid objective type '{tokens[0]}'. Expected 'max' or 'min'", ex);
            }

            // Parse sign-coefficient pairs
            var coefficients = new List<double>();
            for (int i = 1; i < tokens.Count; i++)
            {
                if (!TryParseSignedCoefficient(tokens[i], out double coefficient))
                {
                    throw new InvalidInputException($"Invalid coefficient '{tokens[i]}' in objective function at position {i}. Expected format: +2, -3, etc.");
                }
                coefficients.Add(coefficient);
            }

            return (objectiveType, coefficients);
        }

        /// <summary>
        /// Parses a constraint line
        /// Format: sign1 coeff1 sign2 coeff2 ... <=/>=/= rhs
        /// Example: "+11 +8 +6 <= 40" means 11x1 + 8x2 + 6x3 <= 40
        /// </summary>
        private (List<double> coefficients, ConstraintRelation relation, double rhs) ParseConstraint(string line, int expectedVariables, int constraintIndex)
        {
            if (string.IsNullOrWhiteSpace(line))
                throw new InvalidInputException($"Constraint {constraintIndex + 1} line cannot be empty");

            line = NormaliseSignNumberSpacing(line);

            // Find the relation operator
            var relationMatch = Regex.Matches(line, RelationPattern).Cast<Match>().LastOrDefault();
            if (relationMatch == null)
                throw new InvalidInputException($"Constraint {constraintIndex + 1}: No valid relation operator found (<=, =, >=)");

            string relationStr = relationMatch.Value;
            int relationIndex = relationMatch.Index;

            // Split into LHS and RHS parts
            string lhsPart = line.Substring(0, relationIndex).Trim();
            string rhsPart = line.Substring(relationIndex + relationStr.Length).Trim();

            // Parse relation
            ConstraintRelation relation;
            try
            {
                relation = EnumHelper.ParseRelation(relationStr);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidInputException($"Constraint {constraintIndex + 1}: Invalid relation '{relationStr}'", ex);
            }

            // Parse RHS
            if (!double.TryParse(rhsPart, out double rhs))
            {
                throw new InvalidInputException($"Constraint {constraintIndex + 1}: Invalid RHS value '{rhsPart}'");
            }

            // Parse LHS coefficients as sign-coefficient pairs
            var lhsTokens = SplitLineIntoTokens(lhsPart);
            if (lhsTokens.Count != expectedVariables)
            {
                throw new InvalidInputException($"Constraint {constraintIndex + 1}: Expected {expectedVariables} sign-coefficient pairs, found {lhsTokens.Count}");
            }

            var coefficients = new List<double>();
            for (int i = 0; i < lhsTokens.Count; i++)
            {
                if (!TryParseSignedCoefficient(lhsTokens[i], out double coefficient))
                {
                    throw new InvalidInputException($"Constraint {constraintIndex + 1}: Invalid coefficient '{lhsTokens[i]}' at position {i + 1}. Expected format: +11, -8, etc.");
                }
                coefficients.Add(coefficient);
            }

            return (coefficients, relation, rhs);
        }

        /// <summary>
        /// Parses variable types line
        /// Format: +/-/urs/int/bin +/-/urs/int/bin ...
        /// </summary>
        private List<VariableType> ParseVariableTypes(string line, int expectedCount)
        {
            if (string.IsNullOrWhiteSpace(line))
                throw new InvalidInputException("Variable types line cannot be empty");

            var tokens = SplitLineIntoTokens(line);
            if (tokens.Count != expectedCount)
            {
                throw new InvalidInputException($"Expected {expectedCount} variable types, found {tokens.Count}");
            }

            var variableTypes = new List<VariableType>();
            for (int i = 0; i < tokens.Count; i++)
            {
                try
                {
                    var variableType = EnumHelper.ParseVariableType(tokens[i]);
                    variableTypes.Add(variableType);
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidInputException($"Invalid variable type '{tokens[i]}' at position {i + 1}. Expected: +, -, urs, int, bin", ex);
                }
            }

            return variableTypes;
        }

        /// <summary>
        /// Splits a line into tokens, properly handling whitespace separation
        /// </summary>
        private List<string> SplitLineIntoTokens(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return new List<string>();

            // Split by whitespace and filter out empty entries
            return line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        /// <summary>
        /// Tries to parse a signed coefficient like "+2", "-3", "+11"
        /// </summary>
        private bool TryParseSignedCoefficient(string token, out double coefficient)
        {
            coefficient = 0.0;

            if (string.IsNullOrWhiteSpace(token))
                return false;

            // The token should already include the sign (+ or -)
            if (!token.StartsWith("+") && !token.StartsWith("-"))
            {
                return false;
            }

            return double.TryParse(token, out coefficient);
        }

        /// <summary>
        /// Parses a simple string input for quick testing
        /// Format: "max +2 +3; +1 +1 <= 3; + +"
        /// </summary>
        public LPModel ParseString(string input, string modelName = "String Model")
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("Input string cannot be null or empty", nameof(input));

            // Split by semicolons or newlines
            var lines = input.Split(new char[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            return ParseLines(lines, modelName);
        }

        /// <summary>
        /// Validates file format before parsing
        /// </summary>
        public void ValidateFileFormat(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            var lines = File.ReadAllLines(filePath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToArray();

            if (lines.Length < 3)
                throw new InvalidInputException("File must contain at least 3 lines (objective and variable types)");

            var firstLine = NormaliseSignNumberSpacing(lines[0]);

            // Check first line has max/min
            if (!firstLine.TrimStart().StartsWith("max", StringComparison.OrdinalIgnoreCase) &&
                !firstLine.TrimStart().StartsWith("min", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidInputException("First line must start with 'max' or 'min'");
            }

            // Compute expected variable count from objective line
            var objTokens = SplitLineIntoTokens(firstLine);
            if (objTokens.Count < 2)
                throw new InvalidInputException("Objective line must contain at least one coefficient.");

            int varCount = objTokens.Count - 1;

            // Check last line has valid variable types AND correct count
            var lastLineTokens = SplitLineIntoTokens(lines[lines.Length - 1]);
            if (lastLineTokens.LengthIsNot(varCount))
                throw new InvalidInputException($"Variable types count ({lastLineTokens.Count}) does not match number of objective coefficients ({varCount}).");

            foreach (var token in lastLineTokens)
            {
                if (!IsValidVariableType(token))
                {
                    throw new InvalidInputException($"Invalid variable type '{token}' in last line. Expected: +, -, urs, int, bin");
                }
            }
        }

        /// <summary>
        /// Checks if a token is a valid variable type
        /// </summary>
        private bool IsValidVariableType(string token)
        {
            var validTypes = new[] { "+", "-", "urs", "int", "bin" };
            return validTypes.Contains(token.ToLower());
        }

        /// <summary>
        /// Gets sample input format for help/documentation
        /// </summary>
        public static string GetSampleFormat()
        {
            return @"Sample Input Format:
                    max +2 +3 +3 +5 +2 +4
                    +11 +8 +6 +14 +10 +10 <= 40
                    bin bin bin bin bin bin

                    Where:
                    - First line: max/min followed by signed objective coefficients (+2, -3, etc.)
                    - Middle lines: signed constraint coefficients, relation (<=, =, >=), and RHS
                    - Last line: variable types (+, -, urs, int, bin)
                    - All coefficients must have explicit signs (+/-) 
                    - Variables are interpreted as x1, x2, x3, etc. in order";
        }

        /// <summary>
        /// Creates a sample test file for development
        /// </summary>
        public static void CreateSampleFile(string filePath)
        {
            var sampleContent = @"max +2 +3 +3 +5 +2 +4
                                +11 +8 +6 +14 +10 +10 <= 40
                                bin bin bin bin bin bin";

            File.WriteAllText(filePath, sampleContent);
        }

        /// <summary>
        /// Merge spaced sign-number pairs: "+ 11" -> "+11", "- 3.5" -> "-3.5"
        /// </summary>
        /// <param name="s">String to be merged.</param>
        /// <returns>String of sign and coefficien pairs.</returns>
        private static string NormaliseSignNumberSpacing(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;

            // Only collapses sign followed by whitespace then a number (does not affect the variable types line like "+ bin")
            return Regex.Replace(s, @"([+\-])\s+(\d+(?:\.\d+)?)", "$1$2");
        }
    }

    /// <summary>
    /// Readability in ValidateFileFormat
    /// </summary>
    internal static class ListExtensions
    {
        public static bool LengthIsNot<T>(this IList<T> list, int count) => list.Count != count;
    }
}