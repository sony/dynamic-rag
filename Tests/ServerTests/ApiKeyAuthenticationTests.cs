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
        private const string ValidKey = "test-key-0123456789abcdef0123456789abcdef";
        private const string SecondKey = "rotated-key-fedcba9876543210fedcba9876543210";

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
        public async Task NonBearerAuthorizationHeader_ReturnsNoResult()
        {
            var result = await AuthenticateAsync(r => r.Headers.Authorization = $"Basic {ValidKey}", ValidKey);
            Assert.True(result.None);
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
