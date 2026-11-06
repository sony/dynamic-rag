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
    /// Authenticates requests carrying a valid API key in the <c>X-API-Key</c> header,
    /// in an <c>Authorization: Bearer &lt;key&gt;</c> header (for MCP clients that only support bearer tokens),
    /// or as the password of an <c>Authorization: Basic</c> header (so browsers can open the Swagger UI).
    /// </summary>
    public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
    {
        public const string SchemeName = "ApiKey";
        public const string HeaderName = "X-API-Key";
        private const string BearerPrefix = "Bearer ";
        private const string BasicPrefix = "Basic ";
        private const string BasicRealm = "Dynamic RAG";

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
                else if (authorization.StartsWith(BasicPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryGetBasicPassword(authorization.Substring(BasicPrefix.Length).Trim(), out providedKey))
                    {
                        return Task.FromResult(AuthenticateResult.Fail("Malformed Basic authorization header."));
                    }
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
            if (Context.GetEndpoint() == null)
            {
                // Non-endpoint requests are the Swagger UI / OpenAPI spec (served by middleware). Browsers cannot
                // attach a custom header on navigation, so offer Basic auth: the browser prompts and the user
                // enters the API key as the password (username is ignored).
                Response.Headers.WWWAuthenticate = $"Basic realm=\"{BasicRealm}\", charset=\"UTF-8\"";
            }
            else
            {
                Response.Headers.WWWAuthenticate = $"{SchemeName} header=\"{HeaderName}\"";
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Extracts the password part of a Basic credential (<c>base64(username:password)</c>).
        /// </summary>
        public static bool TryGetBasicPassword(string encodedCredentials, out string? password)
        {
            password = null;
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
                var separator = decoded.IndexOf(':');
                if (separator < 0)
                {
                    return false;
                }
                password = decoded.Substring(separator + 1);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
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
