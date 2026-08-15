using System.Text.RegularExpressions;

namespace PgVectorDynamicRAG.Data
{
    public static partial class SqlIdentifierValidator
    {
        private static readonly Regex SafeIdentifier = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

        // Matches anything outside the identifier allowlist. Applied after the check below as a
        // belt-and-braces strip, so the value handed to callers cannot carry unexpected characters.
        private static readonly Regex DisallowedIdentifierChars =
            new(@"[^a-zA-Z0-9_]", RegexOptions.Compiled);

        /// <summary>
        /// Validates that the input is a safe SQL identifier to prevent SQL injection when used as a table or index name.
        /// </summary>
        public static string Validate(string identifier, string paramName = "identifier")
        {
            if (string.IsNullOrWhiteSpace(identifier) || !SafeIdentifier.IsMatch(identifier))
                throw new ArgumentException($"Invalid SQL identifier: '{identifier}'", paramName);

            // Re-derive the identifier by stripping everything outside the allowlist. This is a
            // no-op for values that passed the check above, and guarantees the returned string
            // contains only characters that are safe to interpolate into an identifier position.
            return DisallowedIdentifierChars.Replace(identifier, string.Empty);
        }
    }
}
