using Xunit;
using DotNetEnv;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PgVectorDynamicRAG.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace PgVectorDynamicRAG.Tests
{
    public class ContextAwareChatControllerTestFixture : IDisposable
    {
        private readonly bool _isVerbose;

        public ContextAwareChatControllerTestFixture()
        {
            _isVerbose = Environment.GetEnvironmentVariable("TEST_VERBOSE")?.ToLower() == "true";

            var testCount = typeof(ContextAwareChatControllerTests)
                .GetMethods()
                .Count(m => m.GetCustomAttributes(typeof(FactAttribute), false).Any());
            
            Console.WriteLine($"\n=== STARTING Context Aware Chat Controller Integration Tests ({testCount} tests) ===\n");

            if (!_isVerbose)
            {
                Console.WriteLine("Running in CLEAN mode (TEST_VERBOSE=false). Set TEST_VERBOSE=true for full ASP.NET logs.\n");
            }
        }

        public void Dispose()
        {
            Console.WriteLine("\n=== FINISHED Context Aware Chat Controller Integration Tests ✅ ===\n");
        }
    }

    public class ContextAwareChatControllerTests : IClassFixture<WebApplicationFactory<ContextAwareChatController>>, IClassFixture<ContextAwareChatControllerTestFixture>
    {
        private readonly HttpClient _client;
        private readonly WebApplicationFactory<ContextAwareChatController> _factory;
        private readonly bool _isVerbose;

        public ContextAwareChatControllerTests(WebApplicationFactory<ContextAwareChatController> factory, ContextAwareChatControllerTestFixture fixture)
        {
            _isVerbose = Environment.GetEnvironmentVariable("TEST_VERBOSE")?.ToLower() == "true";
            
            // Temporarily suppress console output during factory setup in clean mode
            TextWriter? originalOut = null;
            TextWriter? originalError = null;
            
            if (!_isVerbose)
            {
                originalOut = Console.Out;
                originalError = Console.Error;
                Console.SetOut(TextWriter.Null);
                Console.SetError(TextWriter.Null);
            }
            
            try
            {
                _factory = factory.WithWebHostBuilder(builder =>
                {
                    builder.ConfigureServices(services =>
                    {
                        DotNetEnv.Env.Load(Path.Combine(AppContext.BaseDirectory, ".env"));
                    });
                    
                    // Suppress logging for clean mode
                    if (!_isVerbose)
                    {
                        // Suppress ASP.NET Core framework logs
                        builder.UseSetting("Logging:LogLevel:Default", "None");
                        builder.UseSetting("Logging:LogLevel:Microsoft", "None");
                        builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "None");
                        builder.UseSetting("Logging:LogLevel:Microsoft.Hosting", "None");
                        builder.UseSetting("Logging:LogLevel:Microsoft.Extensions", "None");
                        builder.UseSetting("Logging:LogLevel:System", "None");
                        builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore.Hosting", "None");
                        builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore.Mvc", "None");
                        builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore.Routing", "None");
                        
                        // Suppress your application's logs
                        builder.UseSetting("Logging:LogLevel:PgVectorDynamicRAG", "None");
                        
                        builder.ConfigureAppConfiguration((context, config) =>
                        {
                            config.AddInMemoryCollection(new[]
                            {
                                new KeyValuePair<string, string?>("Environment", "Testing"),
                                new KeyValuePair<string, string?>("Logging:LogLevel:Default", "None"),
                                new KeyValuePair<string, string?>("Logging:LogLevel:Microsoft", "None"),
                                new KeyValuePair<string, string?>("Logging:LogLevel:Microsoft.AspNetCore", "None"),
                                new KeyValuePair<string, string?>("Logging:LogLevel:Microsoft.AspNetCore.Hosting", "None"),
                                new KeyValuePair<string, string?>("Logging:LogLevel:Microsoft.AspNetCore.Mvc", "None"),
                                new KeyValuePair<string, string?>("Logging:LogLevel:Microsoft.AspNetCore.Routing", "None"),
                                new KeyValuePair<string, string?>("Logging:LogLevel:PgVectorDynamicRAG", "None")
                            });
                        });
                    }
                });
                
                _client = _factory.CreateClient();
            }
            finally
            {
                // Restore console output
                if (!_isVerbose && originalOut != null && originalError != null)
                {
                    Console.SetOut(originalOut);
                    Console.SetError(originalError);
                }
            }
        }

        [Fact]
        public async Task POST_ChatWithCollection_ReturnsOkWithChatResponse()
        {
            Console.WriteLine($"POST_ChatWithCollection_ReturnsOkWithChatResponse:");
            
            // Use known collection and model from your system (adjust as needed)
            var testCollection = "fandom_transcripts";  // From your existing data
            var testChatModel = "gpt-4o";              // From your available models
            
            // Define test chat scenarios
            var chatScenarios = new[]
            {
                new {
                    testName = "Basic Chat",
                    query = "What information is available in this collection?",
                    nResults = 5,
                    alpha = 0.75,
                    timeWindowEnabled = false,
                    windowDays = 30,
                    temporalDecayEnabled = false,
                    decayImpact = 0.01
                },
                new {
                    testName = "Specific Question",
                    query = "Can you summarize the main topics covered?",
                    nResults = 3,
                    alpha = 0.8,
                    timeWindowEnabled = false,
                    windowDays = 30,
                    temporalDecayEnabled = false,
                    decayImpact = 0.01
                },
                new {
                    testName = "Time Window Enabled",
                    query = "What recent information is available?",
                    nResults = 5,
                    alpha = 0.75,
                    timeWindowEnabled = true,
                    windowDays = 365,
                    temporalDecayEnabled = true,
                    decayImpact = 0.05
                }
            };
            
            Console.WriteLine($"\t[Test Scenarios] Testing {chatScenarios.Length} different chat scenarios...");
            
            foreach (var scenario in chatScenarios)
            {
                Console.WriteLine($"\t[{scenario.testName}] Creating JSON POST request...");
                
                // Create JSON request body
                var chatRequest = new
                {
                    query = scenario.query,
                    collectionName = testCollection,
                    nResults = scenario.nResults,
                    chatModelName = testChatModel,
                    alpha = scenario.alpha,
                    timeWindowEnabled = scenario.timeWindowEnabled,
                    windowDays = scenario.windowDays,
                    temporalDecayEnabled = scenario.temporalDecayEnabled,
                    decayImpact = scenario.decayImpact
                };
                
                var requestContent = new StringContent(
                    JsonSerializer.Serialize(chatRequest),
                    System.Text.Encoding.UTF8,
                    "application/json"
                );
                
                // Make HTTP POST request
                Console.WriteLine($"\t[{scenario.testName}] POST /ContextAwareChat/chat-with-collection");
                var response = await _client.PostAsync("/ContextAwareChat/chat-with-collection", requestContent);
                
                // Assert - HTTP Status Code
                Console.WriteLine($"\t[{scenario.testName}] Checking HTTP status...");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Console.WriteLine($"\t[{scenario.testName}] ✅ HTTP {response.StatusCode}");
                
                // Assert - Content Type
                Console.WriteLine($"\t[{scenario.testName}] Checking response content type...");
                Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
                Console.WriteLine($"\t[{scenario.testName}] ✅ Content-Type: {response.Content.Headers.ContentType}");
                
                // Parse JSON response
                Console.WriteLine($"\t[{scenario.testName}] Reading and parsing JSON...");
                var json = await response.Content.ReadAsStringAsync();
                Assert.False(string.IsNullOrEmpty(json));
                
                var jsonDocument = JsonDocument.Parse(json);
                var root = jsonDocument.RootElement;
                
                // Assert - Response Structure (per API spec)
                Console.WriteLine($"\t[{scenario.testName}] Validating JSON structure...");
                Assert.True(root.TryGetProperty("internal_execution_time", out var timeElement));
                Assert.True(root.TryGetProperty("success", out var successElement));
                Assert.True(root.TryGetProperty("search_results", out var searchResultsElement));
                Assert.True(root.TryGetProperty("query", out var queryElement));
                Assert.True(root.TryGetProperty("final_answer", out var finalAnswerElement));
                Console.WriteLine($"\t[{scenario.testName}] ✅ Has required fields: internal_execution_time, success, search_results, query, final_answer");
                
                // Assert - Data Types and Values
                Console.WriteLine($"\t[{scenario.testName}] Validating field types and values...");
                Assert.True(timeElement.ValueKind == JsonValueKind.Number);
                Assert.True(successElement.ValueKind == JsonValueKind.True);
                Assert.Equal(JsonValueKind.Array, searchResultsElement.ValueKind);
                Assert.Equal(JsonValueKind.String, queryElement.ValueKind);
                Assert.Equal(JsonValueKind.String, finalAnswerElement.ValueKind);
                Console.WriteLine($"\t[{scenario.testName}] ✅ All fields have correct types");
                
                // Validate field content
                Assert.True(successElement.GetBoolean());
                Assert.Equal(scenario.query, queryElement.GetString());
                Assert.False(string.IsNullOrWhiteSpace(finalAnswerElement.GetString()));
                
                // Validate search results structure
                var searchResults = searchResultsElement.EnumerateArray().ToList();
                Console.WriteLine($"\t[{scenario.testName}] Found {searchResults.Count} search results");
                
                if (searchResults.Count > 0)
                {
                    Console.WriteLine($"\t[{scenario.testName}] Validating search result structure...");
                    foreach (var result in searchResults.Take(2)) // Validate first 2 results
                    {
                        Assert.Equal(JsonValueKind.Object, result.ValueKind);
                        Assert.True(result.TryGetProperty("rank", out var rankElement));
                        Assert.True(result.TryGetProperty("score", out var scoreElement));
                        Assert.True(result.TryGetProperty("fileName", out var fileNameElement));
                        Assert.True(result.TryGetProperty("definition", out var definitionElement));
                        
                        // Validate search result field types
                        Assert.True(rankElement.ValueKind == JsonValueKind.Number);
                        Assert.True(scoreElement.ValueKind == JsonValueKind.Number);
                        Assert.Equal(JsonValueKind.String, fileNameElement.ValueKind);
                        Assert.Equal(JsonValueKind.String, definitionElement.ValueKind);
                        
                        // Validate search result content
                        Assert.True(rankElement.GetInt32() > 0);
                        Assert.True(scoreElement.GetDouble() >= 0);
                        Assert.False(string.IsNullOrWhiteSpace(fileNameElement.GetString()));
                        Assert.False(string.IsNullOrWhiteSpace(definitionElement.GetString()));
                    }
                    Console.WriteLine($"\t[{scenario.testName}] ✅ Search results have valid structure");
                }
                else
                {
                    Console.WriteLine($"\t[{scenario.testName}] ⚠️ No search results returned - collection may be empty");
                }
                
                // Assert - Execution Time and Performance
                var executionTime = timeElement.GetDouble();
                Assert.True(executionTime >= 0, "Execution time should be non-negative");
                Assert.True(executionTime < 60, "Execution time should be reasonable (< 60 seconds for chat)");
                Console.WriteLine($"\t[{scenario.testName}] ✅ Execution time: {executionTime:F3} seconds");
                
                // Validate final answer quality
                var finalAnswer = finalAnswerElement.GetString()!;
                Assert.True(finalAnswer.Length > 10, "Final answer should be substantive");
                Console.WriteLine($"\t[{scenario.testName}] ✅ Final answer length: {finalAnswer.Length} characters");
                
                // Log summary for debugging
                Console.WriteLine($"\t[{scenario.testName}] ✅ Chat completed successfully");
                Console.WriteLine($"\t\t- Query: {scenario.query}");
                Console.WriteLine($"\t\t- Search results: {searchResults.Count}");
                Console.WriteLine($"\t\t- Answer preview: {finalAnswer.Substring(0, Math.Min(100, finalAnswer.Length))}...");
                
                if (_isVerbose)
                {
                    // In verbose mode, show the full response
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var formattedJson = JsonSerializer.Serialize(JsonSerializer.Deserialize<object>(json), options);
                    Console.WriteLine($"\t[{scenario.testName}] Full response:\n{formattedJson}");
                }
            }
            
            Console.WriteLine($"\t[POST ChatWithCollection] ✅ All chat integration tests passed");
        }

        [Fact]
        public async Task POST_ChatWithCollection_HandlesErrorScenarios()
        {
            Console.WriteLine($"POST_ChatWithCollection_HandlesErrorScenarios:");
            
            var errorScenarios = new[]
            {
                new {
                    testName = "Invalid Collection Name",
                    request = new {
                        query = "Test query",
                        collectionName = "non_existent_collection_12345",
                        nResults = 5,
                        chatModelName = "gpt-4o"
                    },
                    expectError = true
                },
                new {
                    testName = "Invalid Chat Model",
                    request = new {
                        query = "Test query", 
                        collectionName = "fandom_transcripts",
                        nResults = 5,
                        chatModelName = "invalid_model_name_12345"
                    },
                    expectError = true
                }
            };
            
            foreach (var scenario in errorScenarios)
            {
                Console.WriteLine($"\t[{scenario.testName}] Testing error scenario...");
                
                var requestContent = new StringContent(
                    JsonSerializer.Serialize(scenario.request),
                    System.Text.Encoding.UTF8,
                    "application/json"
                );
                
                var response = await _client.PostAsync("/ContextAwareChat/chat-with-collection", requestContent);
                
                if (scenario.expectError)
                {
                    Console.WriteLine($"\t[{scenario.testName}] Expecting error response...");
                    Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
                    Console.WriteLine($"\t[{scenario.testName}] ✅ Received expected error: {response.StatusCode}");
                }
            }
            
            Console.WriteLine($"\t[Error Handling] ✅ Error scenario testing completed");
        }

        private void Dispose()
        {
            _client?.Dispose();
        }
    }
}