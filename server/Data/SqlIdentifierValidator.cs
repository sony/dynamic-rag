using System.Text.RegularExpressions;

namespace PgVectorDynamicRAG.Data
{
    public static partial class SqlIdentifierValidator
    {
        private static readonly Regex SafeIdentifier = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

        /// <summary>
        /// Validates that the input is a safe SQL identifier to prevent SQL injection when used as a table or index name.
        /// </summary>
        public static string Validate(string identifier, string paramName = "identifier")
        {
            if (string.IsNullOrWhiteSpace(identifier) || !SafeIdentifier.IsMatch(identifier))
                throw new ArgumentException($"Invalid SQL identifier: '{identifier}'", paramName);
            // Rebuild string to break static-analysis taint propagation
            return new string(identifier.ToCharArray());
        }
    }
}
