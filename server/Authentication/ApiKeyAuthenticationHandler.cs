using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PgVectorDynamicRAG.Authentication
{
    /// <summary>
    /// Options for the API key authentication scheme.
    /// </summary>
    public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
    {
        /// <summary>
        /// SHA-256 hashes of the accepted API keys. Keys are hashed so comparisons are
        /// fixed-length and constant-time, and raw keys are not kept around in memory longer than needed.
        /// </summary>
        public IReadOnlyList<byte[]> KeyHashes { get; set; } = Array.Empty<byte[]>();
    }

    /// <summary>
    /// Authenticates requests carrying a valid API key in the <c>X-API-Key</c> header
    /// or in an <c>Authorization: Bearer &lt;key&gt;</c> header (for MCP clients that only support bearer tokens).
    /// </summary>
    public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
    {
        public const string SchemeName = "ApiKey";
        public const string HeaderName = "X-API-Key";
        private const string BearerPrefix = "Bearer ";

        public ApiKeyAuthenticationHandler(
            IOptionsMonitor<ApiKeyAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string? providedKey = null;

            if (Request.Headers.TryGetValue(HeaderName, out var apiKeyHeader))
            {
                providedKey = apiKeyHeader.ToString();
            }
            else
            {
                var authorization = Request.Headers.Authorization.ToString();
                if (authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    providedKey = authorization.Substring(BearerPrefix.Length).Trim();
                }
            }

            if (string.IsNullOrEmpty(providedKey))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            if (!IsValidKey(providedKey, Options.KeyHashes))
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
            }

            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "api-key-client") },
                SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Response.Headers.WWWAuthenticate = $"{SchemeName} header=\"{HeaderName}\"";
            return Task.CompletedTask;
        }

        /// <summary>
        /// Compares the provided key against every configured key hash in constant time.
        /// </summary>
        public static bool IsValidKey(string providedKey, IReadOnlyList<byte[]> keyHashes)
        {
            var providedHash = HashKey(providedKey);
            var match = false;
            foreach (var expected in keyHashes)
            {
                // Evaluate every key (no short-circuit) to avoid leaking which key matched via timing.
                match |= CryptographicOperations.FixedTimeEquals(providedHash, expected);
            }
            return match;
        }

        public static byte[] HashKey(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

        /// <summary>
        /// Reads accepted API keys from the <c>API_KEYS</c> (comma-separated, supports rotation)
        /// and/or <c>API_KEY</c> environment variables.
        /// </summary>
        public static List<string> LoadKeysFromEnvironment()
        {
            var raw = new[]
            {
                Environment.GetEnvironmentVariable("API_KEYS"),
                Environment.GetEnvironmentVariable("API_KEY")
            };

            return raw
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .SelectMany(v => v!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct()
                .ToList();
        }
    }
}
