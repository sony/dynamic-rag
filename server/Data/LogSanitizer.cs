using System.Text.RegularExpressions;

namespace PgVectorDynamicRAG.Data
{
    public static partial class LogSanitizer
    {
        private const int MaxLoggedLength = 256;

        // Matches every character that is not an explicitly allowed printable character.
        // Using an allowlist (rather than blocking \r\n) means no control character can slip through.
        private static readonly Regex DisallowedLogChars =
            new(@"[^a-zA-Z0-9 _\-\./:@,=\+\(\)\[\]]", RegexOptions.Compiled);

        /// <summary>
        /// Strips control characters (notably CR/LF) from a user-supplied value before it is written
        /// to a log, preventing log forging where an attacker injects newlines to fabricate log entries.
        /// The result is also length-capped to keep a single field from flooding the log.
        /// </summary>
        public static string Clean(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var truncated = value.Length > MaxLoggedLength
                ? string.Concat(value.AsSpan(0, MaxLoggedLength), "...")
                : value;

            return DisallowedLogChars.Replace(truncated, "_");
        }
    }
}
