using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PgVectorDynamicRAG.Authentication;
using System.Text.Encodings.Web;

namespace ServerTests
{
    /// <summary>
    /// Unit tests for the API key authentication handler. These do not need a database or model endpoints.
    /// </summary>
    public class ApiKeyAuthenticationTests
    {
        // Random per run, so no credentials are hardcoded in source
        private static readonly string ValidKey = System.Security.Cryptography.RandomNumberGenerator.GetHexString(64);
        private static readonly string SecondKey = System.Security.Cryptography.RandomNumberGenerator.GetHexString(64);

        private static async Task<AuthenticateResult> AuthenticateAsync(Action<HttpRequest> configureRequest, params string[] keys)
        {
            var options = new ApiKeyAuthenticationOptions
            {
                KeyHashes = keys.Select(ApiKeyAuthenticationHandler.HashKey).ToList()
            };
            var monitor = new StaticOptionsMonitor(options);
            var handler = new ApiKeyAuthenticationHandler(monitor, NullLoggerFactory.Instance, UrlEncoder.Default);

            var context = new DefaultHttpContext();
            configureRequest(context.Request);

            var scheme = new AuthenticationScheme(ApiKeyAuthenticationHandler.SchemeName, null, typeof(ApiKeyAuthenticationHandler));
            await handler.InitializeAsync(scheme, context);
            return await handler.AuthenticateAsync();
        }

        [Fact]
        public async Task ValidHeaderKey_Succeeds()
        {
            var result = await AuthenticateAsync(r => r.Headers[ApiKeyAuthenticationHandler.HeaderName] = ValidKey, ValidKey);
            Assert.True(result.Succeeded);
        }

        [Fact]
        public async Task ValidBearerKey_Succeeds()
        {
            var result = await AuthenticateAsync(r => r.Headers.Authorization = $"Bearer {ValidKey}", ValidKey);
            Assert.True(result.Succeeded);
        }

        [Fact]
        public async Task AnyConfiguredKey_Succeeds()
        {
            var result = await AuthenticateAsync(r => r.Headers[ApiKeyAuthenticationHandler.HeaderName] = SecondKey, ValidKey, SecondKey);
            Assert.True(result.Succeeded);
        }

        [Fact]
        public async Task WrongKey_Fails()
        {
            var result = await AuthenticateAsync(r => r.Headers[ApiKeyAuthenticationHandler.HeaderName] = ValidKey + "x", ValidKey);
            Assert.False(result.Succeeded);
            Assert.NotNull(result.Failure);
        }

        [Fact]
        public async Task MissingKey_ReturnsNoResult()
        {
            var result = await AuthenticateAsync(_ => { }, ValidKey);
            Assert.True(result.None);
        }

        [Fact]
        public async Task NoConfiguredKeys_RejectsEverything()
        {
            var result = await AuthenticateAsync(r => r.Headers[ApiKeyAuthenticationHandler.HeaderName] = ValidKey);
            Assert.False(result.Succeeded);
        }

        [Fact]
        public async Task UnsupportedAuthorizationScheme_ReturnsNoResult()
        {
            var result = await AuthenticateAsync(r => r.Headers.Authorization = $"Digest {ValidKey}", ValidKey);
            Assert.True(result.None);
        }

        private static string RandomValue() => System.Security.Cryptography.RandomNumberGenerator.GetHexString(16);

        private static string Basic(string user, string password) =>
            "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user}:{password}"));

        [Fact]
        public async Task ValidBasicPassword_Succeeds()
        {
            var result = await AuthenticateAsync(r => r.Headers.Authorization = Basic(RandomValue(), ValidKey), ValidKey);
            Assert.True(result.Succeeded);
        }

        [Fact]
        public async Task WrongBasicPassword_Fails()
        {
            var result = await AuthenticateAsync(r => r.Headers.Authorization = Basic(ValidKey, RandomValue()), ValidKey);
            Assert.False(result.Succeeded);
        }

        [Theory]
        [InlineData("Basic not-base64!!")]
        [InlineData("Basic bm9jb2xvbg==")] // "nocolon"
        public async Task MalformedBasic_Fails(string header)
        {
            var result = await AuthenticateAsync(r => r.Headers.Authorization = header, ValidKey);
            Assert.False(result.Succeeded);
            Assert.NotNull(result.Failure);
        }

        [Theory]
        [InlineData(false, "Basic realm=")]  // Swagger UI / spec (no endpoint) -> browser login prompt
        [InlineData(true, "ApiKey header=")] // API endpoint -> no browser prompt
        public async Task Challenge_UsesBasicOnlyForNonEndpointRequests(bool hasEndpoint, string expectedPrefix)
        {
            var monitor = new StaticOptionsMonitor(new ApiKeyAuthenticationOptions());
            var handler = new ApiKeyAuthenticationHandler(monitor, NullLoggerFactory.Instance, UrlEncoder.Default);
            var context = new DefaultHttpContext();
            if (hasEndpoint)
            {
                context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, "test"));
            }
            var scheme = new AuthenticationScheme(ApiKeyAuthenticationHandler.SchemeName, null, typeof(ApiKeyAuthenticationHandler));
            await handler.InitializeAsync(scheme, context);

            await handler.ChallengeAsync(new AuthenticationProperties());

            Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
            Assert.StartsWith(expectedPrefix, context.Response.Headers.WWWAuthenticate.ToString());
        }

        private sealed class StaticOptionsMonitor : IOptionsMonitor<ApiKeyAuthenticationOptions>
        {
            private readonly ApiKeyAuthenticationOptions _options;
            public StaticOptionsMonitor(ApiKeyAuthenticationOptions options) => _options = options;
            public ApiKeyAuthenticationOptions CurrentValue => _options;
            public ApiKeyAuthenticationOptions Get(string? name) => _options;
            public IDisposable? OnChange(Action<ApiKeyAuthenticationOptions, string?> listener) => null;
        }
    }
}
