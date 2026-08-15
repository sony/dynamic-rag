using PgVectorDynamicRAG.Data;

namespace ServerTests
{
    /// <summary>
    /// Tests for the input-validation and log-sanitization helpers that guard the
    /// SQL identifier, storage key and logging sinks against injection.
    /// </summary>
    public class LogSanitizerTests
    {
        [Theory]
        [InlineData("plain-collection", "plain-collection")]
        [InlineData("with\rcarriage", "with_carriage")]
        [InlineData("with\nnewline", "with_newline")]
        [InlineData("with\r\nboth", "with__both")]
        [InlineData("with\ttab", "with_tab")]
        [InlineData("null\0byte", "null_byte")]
        public void Clean_StripsControlCharacters(string input, string expected)
        {
            Assert.Equal(expected, LogSanitizer.Clean(input));
        }

        [Fact]
        public void Clean_NeutralizesForgedLogEntry()
        {
            // A classic log-forging payload: terminate the line, then fake a new entry.
            var forged = "docs\r\n[WARN] admin login succeeded";
            var cleaned = LogSanitizer.Clean(forged);

            Assert.DoesNotContain("\r", cleaned);
            Assert.DoesNotContain("\n", cleaned);
        }

        [Fact]
        public void Clean_CapsLength()
        {
            var cleaned = LogSanitizer.Clean(new string('a', 5000));

            // 256 retained characters plus the truncation marker.
            Assert.Equal(259, cleaned.Length);
            Assert.EndsWith("...", cleaned);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Clean_HandlesEmptyInput(string? input)
        {
            Assert.Equal(string.Empty, LogSanitizer.Clean(input));
        }
    }

    public class StoragePathValidatorTests
    {
        [Theory]
        [InlineData("reports/2024/q1.pdf")]
        [InlineData("single.txt")]
        [InlineData("nested/dir/file name.docx")]
        public void ValidateRelativePath_AcceptsSafePaths(string path)
        {
            Assert.Equal(path, StoragePathValidator.ValidateRelativePath(path));
        }

        [Theory]
        [InlineData("../secrets/key.pem")]
        [InlineData("reports/../../etc/passwd")]
        [InlineData("reports/./nested")]
        [InlineData("..\\windows\\system32")]
        [InlineData("/absolute/path.txt")]
        [InlineData("with\r\nnewline.txt")]
        [InlineData("")]
        [InlineData("   ")]
        public void ValidateRelativePath_RejectsTraversalAndAbsolutePaths(string path)
        {
            Assert.Throws<ArgumentException>(() => StoragePathValidator.ValidateRelativePath(path));
        }

        [Fact]
        public void ValidateRelativePath_NormalizesBackslashes()
        {
            Assert.Equal("reports/q1.pdf", StoragePathValidator.ValidateRelativePath("reports\\q1.pdf"));
        }

        [Theory]
        [InlineData("report.pdf", "report.pdf")]
        [InlineData("../../escape.txt", "escape.txt")]
        [InlineData("/leading/dir/file.txt", "file.txt")]
        [InlineData("nested\\windows\\file.txt", "file.txt")]
        public void ValidateFileName_StripsDirectoryComponents(string input, string expected)
        {
            Assert.Equal(expected, StoragePathValidator.ValidateFileName(input));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("..")]
        [InlineData("bad\nname.txt")]
        public void ValidateFileName_RejectsInvalidNames(string fileName)
        {
            Assert.Throws<ArgumentException>(() => StoragePathValidator.ValidateFileName(fileName));
        }
    }

    public class SqlIdentifierValidatorTests
    {
        [Theory]
        [InlineData("documents")]
        [InlineData("_private")]
        [InlineData("collection_1")]
        public void Validate_AcceptsSafeIdentifiers(string identifier)
        {
            Assert.Equal(identifier, SqlIdentifierValidator.Validate(identifier));
        }

        [Theory]
        [InlineData("docs; DROP TABLE users--")]
        [InlineData("docs\" OR \"1\"=\"1")]
        [InlineData("1_starts_with_digit")]
        [InlineData("has-hyphen")]
        [InlineData("has space")]
        [InlineData("has\r\nnewline")]
        [InlineData("")]
        public void Validate_RejectsInjectionPayloads(string identifier)
        {
            Assert.Throws<ArgumentException>(() => SqlIdentifierValidator.Validate(identifier));
        }
    }
}
