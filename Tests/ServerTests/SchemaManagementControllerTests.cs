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
    public class SchemaManagementControllerTestFixture : IDisposable
    {

        private readonly bool _isVerbose;

        public SchemaManagementControllerTestFixture()
        {
            _isVerbose = Environment.GetEnvironmentVariable("TEST_VERBOSE")?.ToLower() == "true";

            var testCount = typeof(SchemaManagementControllerTests)
                .GetMethods()
                .Count(m => m.GetCustomAttributes(typeof(FactAttribute), false).Any());
            
            Console.WriteLine($"\n=== STARTING Schema Management Controller Integration Tests ({testCount} tests) ===\n");

            if (!_isVerbose)
            {
                Console.WriteLine("Running in CLEAN mode (TEST_VERBOSE=false). Set TEST_VERBOSE=true for full ASP.NET logs.\n");
            }
        }

        public void Dispose()
        {
            Console.WriteLine("\n=== FINISHED Schema Management Controller Integration Tests ✅ ===\n");
        }
    }

    public class SchemaManagementControllerTests : IClassFixture<WebApplicationFactory<SchemaManagementController>>, IClassFixture<SchemaManagementControllerTestFixture>
    {
        private readonly HttpClient _client;
        private readonly WebApplicationFactory<SchemaManagementController> _factory;
        private readonly bool _isVerbose;

        public SchemaManagementControllerTests(WebApplicationFactory<SchemaManagementController> factory, SchemaManagementControllerTestFixture fixture)
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
        public async Task GET_ReadChatModels_ReturnsOkWithChatModels()
        {
            Console.WriteLine($"GET_ReadChatModels_ReturnsOkWithChatModels:");
            
            // Act - Make real HTTP GET request
            Console.WriteLine($"\t[HTTP Request] GET /SchemaManagement/read-chat-models");
            var response = await _client.GetAsync("/SchemaManagement/read-chat-models");
            
            // Assert - HTTP Status Code
            Console.WriteLine($"\t[Status Code] Checking HTTP status...");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Console.WriteLine($"\t[Status Code] ✅ Received {response.StatusCode}");
            
            // Assert - Content Type
            Console.WriteLine($"\t[Content Type] Checking response content type...");
            Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
            Console.WriteLine($"\t[Content Type] ✅ Content-Type: {response.Content.Headers.ContentType}");
            
            // Assert - Response Body
            Console.WriteLine($"\t[Response Body] Reading and parsing JSON...");
            var json = await response.Content.ReadAsStringAsync();
            Assert.False(string.IsNullOrEmpty(json));
            
            // Parse JSON response
            var jsonDocument = JsonDocument.Parse(json);
            var root = jsonDocument.RootElement;
            
            // Assert - Response Structure (based on your controller code)
            Console.WriteLine($"\t[Response Structure] Validating JSON structure...");
            Assert.True(root.TryGetProperty("chat_models", out var chatModelsElement));
            Assert.True(root.TryGetProperty("internal_execution_time", out var timeElement));
            Console.WriteLine($"\t[Response Structure] ✅ Has required fields: chat_models, internal_execution_time");
            
            // Assert - Data Types
            Console.WriteLine($"\t[Data Types] Validating field types...");
            Assert.Equal(JsonValueKind.Array, chatModelsElement.ValueKind);
            Assert.True(timeElement.ValueKind == JsonValueKind.Number);
            Console.WriteLine($"\t[Data Types] ✅ chat_models is array, internal_execution_time is number");
            
            // Assert - Data Quality
            var chatModels = chatModelsElement.EnumerateArray().ToList();
            Console.WriteLine($"\t[Data Quality] Found {chatModels.Count} chat models");
            
            if (chatModels.Count > 0)
            {
                Console.WriteLine($"\t[Data Quality] Validating model data...");
                foreach (var model in chatModels)
                {
                    Assert.Equal(JsonValueKind.String, model.ValueKind);
                    Assert.False(string.IsNullOrWhiteSpace(model.GetString()));
                }
                Console.WriteLine($"\t[Data Quality] ✅ All models are valid strings");
                
                // Log the actual models found
                var modelNames = chatModels.Select(m => m.GetString()).ToList();
                foreach (var modelName in modelNames)
                {
                    Console.WriteLine($"\t\t- {modelName}");
                }
            }
            else
            {
                Console.WriteLine($"\t[Data Quality] ✅ Empty response (no models in database)");
            }
            
            // Assert - Execution Time
            var executionTime = timeElement.GetDouble();
            Assert.True(executionTime >= 0, "Execution time should be non-negative");
            Assert.True(executionTime < 30, "Execution time should be reasonable (< 30 seconds)");
            Console.WriteLine($"\t[Performance] ✅ Execution time: {executionTime:F3} seconds");
            
            // Log full response for debugging
            var options = new JsonSerializerOptions { WriteIndented = true };
            var formattedJson = JsonSerializer.Serialize(JsonSerializer.Deserialize<object>(json), options);
            Console.WriteLine($"\t[Full Response]\n{formattedJson}");
            Console.WriteLine($"\t[GET ReadChatModels] ✅ All integration tests passed");
        }

        [Fact]
        public async Task GET_ReadEmbeddingModels_ReturnsOkWithEmbeddingModels()
        {
            Console.WriteLine($"GET_ReadEmbeddingModels_ReturnsOkWithEmbeddingModels:");
            
            // Act - Make real HTTP GET request
            Console.WriteLine($"\t[HTTP Request] GET /SchemaManagement/read-embedding-models");
            var response = await _client.GetAsync("/SchemaManagement/read-embedding-models");
            
            // Assert - HTTP Status Code
            Console.WriteLine($"\t[Status Code] Checking HTTP status...");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Console.WriteLine($"\t[Status Code] ✅ Received {response.StatusCode}");
            
            // Assert - Content Type
            Console.WriteLine($"\t[Content Type] Checking response content type...");
            Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
            Console.WriteLine($"\t[Content Type] ✅ Content-Type: {response.Content.Headers.ContentType}");
            
            // Assert - Response Body
            Console.WriteLine($"\t[Response Body] Reading and parsing JSON...");
            var json = await response.Content.ReadAsStringAsync();
            Assert.False(string.IsNullOrEmpty(json));
            
            // Parse JSON response
            var jsonDocument = JsonDocument.Parse(json);
            var root = jsonDocument.RootElement;
            
            // Assert - Response Structure (based on your controller code)
            Console.WriteLine($"\t[Response Structure] Validating JSON structure...");
            Assert.True(root.TryGetProperty("embedding_models", out var embeddingModelsElement));
            Assert.True(root.TryGetProperty("internal_execution_time", out var timeElement));
            Console.WriteLine($"\t[Response Structure] ✅ Has required fields: embedding_models, internal_execution_time");
            
            // Assert - Data Types
            Console.WriteLine($"\t[Data Types] Validating field types...");
            Assert.Equal(JsonValueKind.Object, embeddingModelsElement.ValueKind);
            Assert.True(timeElement.ValueKind == JsonValueKind.Number);
            Console.WriteLine($"\t[Data Types] ✅ embedding_models is object, internal_execution_time is number");
            
            // Assert - Data Quality (Dictionary structure)
            Console.WriteLine($"\t[Data Quality] Found {embeddingModelsElement.EnumerateObject().Count()} modalities");
            
            if (embeddingModelsElement.EnumerateObject().Any())
            {
                Console.WriteLine($"\t[Data Quality] Validating modality data...");
                foreach (var modality in embeddingModelsElement.EnumerateObject())
                {
                    Assert.False(string.IsNullOrWhiteSpace(modality.Name));
                    Assert.Equal(JsonValueKind.Array, modality.Value.ValueKind);
                    
                    var models = modality.Value.EnumerateArray().ToList();
                    Assert.NotEmpty(models);
                    
                    foreach (var model in models)
                    {
                        Assert.Equal(JsonValueKind.String, model.ValueKind);
                        Assert.False(string.IsNullOrWhiteSpace(model.GetString()));
                    }
                }
                Console.WriteLine($"\t[Data Quality] ✅ All modalities have valid structure");
                
                // Log the actual modalities and models found
                foreach (var modality in embeddingModelsElement.EnumerateObject())
                {
                    var modelNames = modality.Value.EnumerateArray().Select(m => m.GetString()).ToList();
                    Console.WriteLine($"\t\t- {modality.Name}: {string.Join(", ", modelNames)}");
                }
            }
            else
            {
                Console.WriteLine($"\t[Data Quality] ✅ Empty response (no embedding models in database)");
            }
            
            // Assert - Execution Time
            var executionTime = timeElement.GetDouble();
            Assert.True(executionTime >= 0, "Execution time should be non-negative");
            Assert.True(executionTime < 30, "Execution time should be reasonable (< 30 seconds)");
            Console.WriteLine($"\t[Performance] ✅ Execution time: {executionTime:F3} seconds");
            
            // Log full response for debugging
            var options = new JsonSerializerOptions { WriteIndented = true };
            var formattedJson = JsonSerializer.Serialize(JsonSerializer.Deserialize<object>(json), options);
            Console.WriteLine($"\t[Full Response]\n{formattedJson}");
            Console.WriteLine($"\t[GET ReadEmbeddingModels] ✅ All integration tests passed");
        }

        [Fact]
        public async Task GET_ReadCollections_ReturnsOkWithCollections()
        {
            Console.WriteLine($"GET_ReadCollections_ReturnsOkWithCollections:");
            
            // Act - Make real HTTP GET request
            Console.WriteLine($"\t[HTTP Request] GET /SchemaManagement/read-collections");
            var response = await _client.GetAsync("/SchemaManagement/read-collections");
            
            // Assert - HTTP Status Code
            Console.WriteLine($"\t[Status Code] Checking HTTP status...");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Console.WriteLine($"\t[Status Code] ✅ Received {response.StatusCode}");
            
            // Assert - Content Type
            Console.WriteLine($"\t[Content Type] Checking response content type...");
            Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
            Console.WriteLine($"\t[Content Type] ✅ Content-Type: {response.Content.Headers.ContentType}");
            
            // Assert - Response Body
            Console.WriteLine($"\t[Response Body] Reading and parsing JSON...");
            var json = await response.Content.ReadAsStringAsync();
            Assert.False(string.IsNullOrEmpty(json));
            
            // Parse JSON response
            var jsonDocument = JsonDocument.Parse(json);
            var root = jsonDocument.RootElement;
            
            // Assert - Response Structure (based on your controller code)
            Console.WriteLine($"\t[Response Structure] Validating JSON structure...");
            Assert.True(root.TryGetProperty("collections", out var collectionsElement));
            Assert.True(root.TryGetProperty("internal_execution_time", out var timeElement));
            Console.WriteLine($"\t[Response Structure] ✅ Has required fields: collections, internal_execution_time");
            
            // Assert - Data Types
            Console.WriteLine($"\t[Data Types] Validating field types...");
            Assert.Equal(JsonValueKind.Array, collectionsElement.ValueKind);
            Assert.True(timeElement.ValueKind == JsonValueKind.Number);
            Console.WriteLine($"\t[Data Types] ✅ collections is array, internal_execution_time is number");
            
            // Assert - Data Quality
            var collections = collectionsElement.EnumerateArray().ToList();
            Console.WriteLine($"\t[Data Quality] Found {collections.Count} collections");
            
            if (collections.Count > 0)
            {
                Console.WriteLine($"\t[Data Quality] Validating collection data...");
                foreach (var collection in collections)
                {
                    Assert.Equal(JsonValueKind.Object, collection.ValueKind);
                    
                    // Check required fields exist
                    Assert.True(collection.TryGetProperty("collection_name", out var nameElement));
                    Assert.True(collection.TryGetProperty("collection_type", out var typeElement));
                    Assert.True(collection.TryGetProperty("default_embedding_service_id", out var serviceElement));
                    Assert.True(collection.TryGetProperty("default_distance_metric", out var metricElement));
                    Assert.True(collection.TryGetProperty("description", out var descElement));
                    
                    // Check field values
                    Assert.Equal(JsonValueKind.String, nameElement.ValueKind);
                    Assert.False(string.IsNullOrWhiteSpace(nameElement.GetString()));
                    Assert.Equal(JsonValueKind.String, typeElement.ValueKind);
                    Assert.False(string.IsNullOrWhiteSpace(typeElement.GetString()));
                    Assert.Equal(JsonValueKind.String, serviceElement.ValueKind);
                    Assert.False(string.IsNullOrWhiteSpace(serviceElement.GetString()));
                    Assert.Equal(JsonValueKind.String, metricElement.ValueKind);
                    Assert.False(string.IsNullOrWhiteSpace(metricElement.GetString()));
                    Assert.Equal(JsonValueKind.String, descElement.ValueKind);
                    // Note: description can be empty string, but not null
                }
                Console.WriteLine($"\t[Data Quality] ✅ All collections have valid structure");
                
                // Log the actual collections found
                foreach (var collection in collections)
                {
                    var name = collection.GetProperty("collection_name").GetString();
                    var type = collection.GetProperty("collection_type").GetString();
                    Console.WriteLine($"\t\t- {name} ({type})");
                }
            }
            else
            {
                Console.WriteLine($"\t[Data Quality] ✅ Empty response (no collections in database)");
            }
            
            // Assert - Execution Time
            var executionTime = timeElement.GetDouble();
            Assert.True(executionTime >= 0, "Execution time should be non-negative");
            Assert.True(executionTime < 30, "Execution time should be reasonable (< 30 seconds)");
            Console.WriteLine($"\t[Performance] ✅ Execution time: {executionTime:F3} seconds");
            
            // Log full response for debugging
            var options = new JsonSerializerOptions { WriteIndented = true };
            var formattedJson = JsonSerializer.Serialize(JsonSerializer.Deserialize<object>(json), options);
            Console.WriteLine($"\t[Full Response]\n{formattedJson}");
            Console.WriteLine($"\t[GET ReadCollections] ✅ All integration tests passed");
        }

        private void Dispose()
        {
            _client?.Dispose();
        }
    }
}