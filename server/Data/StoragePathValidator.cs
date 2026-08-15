namespace PgVectorDynamicRAG.Data
{
    public static class StoragePathValidator
    {
        private static readonly char[] Separators = { '/', '\\' };

        /// <summary>
        /// Validates a caller-supplied relative path (blob path / object key) before it is combined
        /// into a storage key. Rejects absolute paths, traversal segments and control characters so a
        /// request cannot address blobs outside its intended collection prefix.
        /// </summary>
        public static string ValidateRelativePath(string path, string paramName = "path")
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path must not be empty.", paramName);

            if (path.Any(char.IsControl))
                throw new ArgumentException("Path must not contain control characters.", paramName);

            // Backslashes are normalised to separators by some storage providers, so treat them the
            // same as '/' rather than letting them slip past the traversal check below.
            var normalized = path.Replace('\\', '/');

            if (normalized.StartsWith('/'))
                throw new ArgumentException("Path must be relative, not absolute.", paramName);

            foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == "." || segment == "..")
                    throw new ArgumentException($"Path must not contain traversal segments: '{path}'", paramName);
            }

            return normalized;
        }

        /// <summary>
        /// Validates an uploaded file's name before it is used as the final segment of a storage key.
        /// Any directory component is stripped, so a crafted client-supplied name cannot redirect the
        /// upload to another prefix.
        /// </summary>
        public static string ValidateFileName(string fileName, string paramName = "fileName")
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("File name must not be empty.", paramName);

            if (fileName.Any(char.IsControl))
                throw new ArgumentException("File name must not contain control characters.", paramName);

            // Keep only the leaf name; discards "../" prefixes and any directory the client supplied.
            var leaf = fileName.Split(Separators, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();

            if (string.IsNullOrWhiteSpace(leaf) || leaf == "." || leaf == "..")
                throw new ArgumentException($"Invalid file name: '{fileName}'", paramName);

            return leaf;
        }
    }
}
