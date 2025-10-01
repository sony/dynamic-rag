using Xunit;
using DotNetEnv;
using System.Net.Sockets;
using PgVectorDynamicRAG.Data;
using PgVectorDynamicRAG.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace PgVectorDynamicRAG.Tests
{
    public class SchemaManagementTestFixture : IDisposable
    {
        public SchemaManagementTestFixture()
        {
            var testCount = typeof(SchemaManagementServiceTests)
                .GetMethods()
                .Count(m => m.GetCustomAttributes(typeof(FactAttribute), false).Any());
            
            Console.WriteLine($"\n=== STARTING Schema Management Service Unit Tests ({testCount} tests) ===\n");
        }

        public void Dispose()
        {
            Console.WriteLine("\n=== FINISHED Schema Management Service Unit Tests ✅ ===\n");
        }
    }

    public class SchemaManagementServiceTests : IClassFixture<SchemaManagementTestFixture>
    {
        private readonly SchemaManagementService _schemaManagementService;
        private readonly ILogger<SchemaManagementService> _logger;

        public SchemaManagementServiceTests(SchemaManagementTestFixture fixture)
        {
            DotNetEnv.Env.Load(Path.Combine(AppContext.BaseDirectory, ".env"));

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger<SchemaManagementService>();

            var configuration = new ConfigurationBuilder().Build();
            var connectionFactory = new PostgresConnectionFactory(configuration);
            _schemaManagementService = new SchemaManagementService(_logger, connectionFactory);
        }

        /// <summary>
        /// Tests the ReadChatModels functionality to retrieve available chat models from the database.
        /// Validates data quality by ensuring all model names are valid non-empty strings, tests
        /// service resilience with repeat calls, and handles empty database scenarios gracefully.
        /// Covers happy path scenarios with comprehensive model name validation and logging.
        /// </summary>
        [Fact]
        public async Task ReadChatModels_HappyPathAndDataValidation()
        {
            Console.WriteLine($"ReadChatModels_HappyPathAndDataValidation:");
            
            // Happy Path
            Console.WriteLine($"\t[Happy Path] Testing normal functionality...");
            var result = await _schemaManagementService.ReadChatModels();
            
            Assert.NotNull(result);
            Assert.IsType<List<string>>(result);
            Console.WriteLine($"\t[Happy Path] ✅ Returned {result.Count} chat models");
            
            // Data Quality Validation
            if (result.Count > 0)
            {
                Console.WriteLine($"\t[Data Quality] Validating model names...");
                Assert.All(result, model => 
                {
                    Assert.NotNull(model);
                    Assert.NotEmpty(model);
                    Assert.False(string.IsNullOrWhiteSpace(model), "Model name should not be whitespace");
                });
                Console.WriteLine($"\t[Data Quality] ✅ All {result.Count} models have valid names");
                
                foreach (var model in result)
                {
                    Console.WriteLine($"\t\t- {model}");
                }
            }
            else
            {
                Console.WriteLine($"\t[Empty Database] ✅ Correctly handles empty chat model table");
            }
            
            // Service Resilience
            Console.WriteLine($"\t[Resilience] Testing repeat calls...");
            var secondCall = await _schemaManagementService.ReadChatModels();
            Assert.NotNull(secondCall);
            Assert.Equal(result.Count, secondCall.Count);
            Console.WriteLine($"\t[Resilience] ✅ Service handles repeat calls correctly");
            
            Console.WriteLine($"\t[ReadChatModels] ✅ Happy path scenarios tested successfully");
        }

        /// <summary>
        /// Tests the ReadEmbeddingModels functionality to retrieve embedding models grouped by modality
        /// (TextEmbedding, ImageEmbedding, etc.). Validates dictionary structure, modality names,
        /// and individual model names within each group. Tests service resilience and handles
        /// empty database scenarios while providing detailed modality and model reporting.
        /// </summary>
        [Fact]
        public async Task ReadEmbeddingModels_HappyPathAndDataValidation()
        {
            Console.WriteLine($"ReadEmbeddingModels_HappyPathAndDataValidation:");
            
            // Happy Path
            Console.WriteLine($"\t[Happy Path] Testing normal functionality...");
            var result = await _schemaManagementService.ReadEmbeddingModels();
            
            Assert.NotNull(result);
            Assert.IsType<Dictionary<string, List<string>>>(result);
            Console.WriteLine($"\t[Happy Path] ✅ Returned {result.Count} embedding model modalities");
            
            // Data Quality Validation
            if (result.Count > 0)
            {
                Console.WriteLine($"\t[Data Quality] Validating modalities and model names...");
                foreach (var modalityGroup in result)
                {
                    Assert.NotNull(modalityGroup.Key);
                    Assert.NotEmpty(modalityGroup.Key);
                    Assert.False(string.IsNullOrWhiteSpace(modalityGroup.Key), "Modality name should not be whitespace");
                    Assert.NotNull(modalityGroup.Value);
                    Assert.NotEmpty(modalityGroup.Value);
                    
                    Assert.All(modalityGroup.Value, model =>
                    {
                        Assert.NotNull(model);
                        Assert.NotEmpty(model);
                        Assert.False(string.IsNullOrWhiteSpace(model), "Model name should not be whitespace");
                    });
                }
                Console.WriteLine($"\t[Data Quality] ✅ All {result.Count} modalities have valid data");
                
                foreach (var group in result)
                {
                    Console.WriteLine($"\t\t- {group.Key}: {string.Join(", ", group.Value)}");
                }
            }
            else
            {
                Console.WriteLine($"\t[Empty Database] ✅ Correctly handles empty embedding model table");
            }
            
            // Service Resilience
            Console.WriteLine($"\t[Resilience] Testing repeat calls...");
            var secondCall = await _schemaManagementService.ReadEmbeddingModels();
            Assert.NotNull(secondCall);
            Assert.Equal(result.Count, secondCall.Count);
            Console.WriteLine($"\t[Resilience] ✅ Service handles repeat calls correctly");
            
            Console.WriteLine($"\t[ReadEmbeddingModels] ✅ Happy path scenarios tested successfully");
        }

        /// <summary>
        /// Tests the ReadCollections functionality to retrieve all vector database collections with
        /// their metadata. Validates required fields (collection_name, collection_type, 
        /// default_embedding_service_id, default_distance_metric, description), ensures proper
        /// data types and non-null values, and tests service resilience with comprehensive logging.
        /// </summary>
        [Fact]
        public async Task ReadCollections_HappyPathAndDataValidation()
        {
            Console.WriteLine($"ReadCollections_HappyPathAndDataValidation:");
            
            // Happy Path
            Console.WriteLine($"\t[Happy Path] Testing normal functionality...");
            var result = await _schemaManagementService.ReadCollections();
            
            Assert.NotNull(result);
            Assert.IsType<List<Dictionary<string, string>>>(result);
            Console.WriteLine($"\t[Happy Path] ✅ Returned {result.Count} collections");
            
            // Data Quality Validation
            if (result.Count > 0)
            {
                Console.WriteLine($"\t[Data Quality] Validating collection details...");
                Assert.All(result, collection =>
                {
                    Assert.NotNull(collection);
                    
                    // Test required fields exist
                    Assert.True(collection.ContainsKey("collection_name"), "Collection should have collection_name");
                    Assert.True(collection.ContainsKey("collection_type"), "Collection should have collection_type");
                    Assert.True(collection.ContainsKey("default_embedding_service_id"), "Collection should have default_embedding_service_id");
                    Assert.True(collection.ContainsKey("default_distance_metric"), "Collection should have default_distance_metric");
                    Assert.True(collection.ContainsKey("description"), "Collection should have description");
                    
                    // Test field values aren't null/empty (except description can be empty)
                    Assert.NotNull(collection["collection_name"]);
                    Assert.NotEmpty(collection["collection_name"]);
                    Assert.False(string.IsNullOrWhiteSpace(collection["collection_name"]), "Collection name should not be whitespace");
                    
                    Assert.NotNull(collection["collection_type"]);
                    Assert.NotEmpty(collection["collection_type"]);
                    Assert.False(string.IsNullOrWhiteSpace(collection["collection_type"]), "Collection type should not be whitespace");
                    
                    Assert.NotNull(collection["default_embedding_service_id"]);
                    Assert.NotEmpty(collection["default_embedding_service_id"]);
                    
                    Assert.NotNull(collection["default_distance_metric"]);
                    Assert.NotEmpty(collection["default_distance_metric"]);
                    
                    Assert.NotNull(collection["description"]); // Can be empty string, but not null
                });
                Console.WriteLine($"\t[Data Quality] ✅ All {result.Count} collections have valid structure");
                
                foreach (var collection in result)
                {
                    Console.WriteLine($"\t\t- {collection["collection_name"]} ({collection["collection_type"]})");
                }
            }
            else
            {
                Console.WriteLine($"\t[Empty Database] ✅ Correctly handles empty collections table");
            }
            
            // Service Resilience
            Console.WriteLine($"\t[Resilience] Testing repeat calls...");
            var secondCall = await _schemaManagementService.ReadCollections();
            Assert.NotNull(secondCall);
            Assert.Equal(result.Count, secondCall.Count);
            Console.WriteLine($"\t[Resilience] ✅ Service handles repeat calls correctly");
            
            Console.WriteLine($"\t[ReadCollections] ✅ Happy path scenarios tested successfully");
        }

        /// <summary>
        /// Comprehensive error handling test that validates all SchemaManagement APIs respond
        /// appropriately to connection failures, authentication errors, and database errors.
        /// Tests network connectivity issues, invalid credentials, and non-existent databases
        /// across all three APIs (ReadChatModels, ReadEmbeddingModels, ReadCollections) with
        /// proper exception type validation and meaningful error messages.
        /// </summary>
        [Fact]
        public async Task AllAPIs_ErrorHandling_ShouldReturnMeaningfulErrors()
        {
            Console.WriteLine($"AllAPIs_ErrorHandling_ShouldReturnMeaningfulErrors:");
            
            // Test each API for connection errors
            await TestConnectionErrors();
            await TestAuthenticationErrors();
            await TestDatabaseErrors();
            
            Console.WriteLine($"\t[All APIs] ✅ Error handling tested successfully");
        }

        private async Task TestConnectionErrors()
        {
            Console.WriteLine($"\t[Connection Errors] Testing all APIs with invalid connection...");
            var originalHost = Environment.GetEnvironmentVariable("PG_HOST");
            
            try
            {
                Environment.SetEnvironmentVariable("PG_HOST", "definitely-invalid-host-12345.com");
                var config = new ConfigurationBuilder().Build();
                var factory = new PostgresConnectionFactory(config);
                var badService = new SchemaManagementService(_logger, factory);
                
                // Test ReadChatModels
                try
                {
                    await badService.ReadChatModels();
                    Assert.Fail("ReadChatModels: Expected connection error but none was thrown");
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"\t\t[ReadChatModels] ✅ SocketException: {ex.Message}");
                }
                
                // Test ReadEmbeddingModels
                try
                {
                    await badService.ReadEmbeddingModels();
                    Assert.Fail("ReadEmbeddingModels: Expected connection error but none was thrown");
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"\t\t[ReadEmbeddingModels] ✅ SocketException: {ex.Message}");
                }
                
                // Test ReadCollections
                try
                {
                    await badService.ReadCollections();
                    Assert.Fail("ReadCollections: Expected connection error but none was thrown");
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"\t\t[ReadCollections] ✅ SocketException: {ex.Message}");
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("PG_HOST", originalHost);
            }
        }

        private async Task TestAuthenticationErrors()
        {
            Console.WriteLine($"\t[Auth Errors] Testing all APIs with invalid credentials...");
            var originalUser = Environment.GetEnvironmentVariable("PG_USER");
            
            try
            {
                Environment.SetEnvironmentVariable("PG_USER", "fake_user_12345");
                var config = new ConfigurationBuilder().Build();
                var factory = new PostgresConnectionFactory(config);
                var badService = new SchemaManagementService(_logger, factory);
                
                // Test all 3 APIs
                foreach (var apiName in new[] { "ReadChatModels", "ReadEmbeddingModels", "ReadCollections" })
                {
                    try
                    {
                        switch (apiName)
                        {
                            case "ReadChatModels":
                                await badService.ReadChatModels();
                                break;
                            case "ReadEmbeddingModels":
                                await badService.ReadEmbeddingModels();
                                break;
                            case "ReadCollections":
                                await badService.ReadCollections();
                                break;
                        }
                        Assert.Fail($"{apiName}: Expected auth error but none was thrown");
                    }
                    catch (Exception ex) when (ex.GetType().Name == "PostgresException")
                    {
                        Assert.Contains("password authentication failed", ex.Message.ToLower());
                        Console.WriteLine($"\t\t[{apiName}] ✅ PostgresException: {ex.Message}");
                    }
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("PG_USER", originalUser);
            }
        }

        private async Task TestDatabaseErrors()
        {
            Console.WriteLine($"\t[Database Errors] Testing all APIs with invalid database...");
            var originalDb = Environment.GetEnvironmentVariable("PG_DATABASE");
            
            try
            {
                Environment.SetEnvironmentVariable("PG_DATABASE", "nonexistent_db_12345");
                var config = new ConfigurationBuilder().Build();
                var factory = new PostgresConnectionFactory(config);
                var badService = new SchemaManagementService(_logger, factory);
                
                // Test all 3 APIs
                foreach (var apiName in new[] { "ReadChatModels", "ReadEmbeddingModels", "ReadCollections" })
                {
                    try
                    {
                        switch (apiName)
                        {
                            case "ReadChatModels":
                                await badService.ReadChatModels();
                                break;
                            case "ReadEmbeddingModels":
                                await badService.ReadEmbeddingModels();
                                break;
                            case "ReadCollections":
                                await badService.ReadCollections();
                                break;
                        }
                        Assert.Fail($"{apiName}: Expected database error but none was thrown");
                    }
                    catch (Exception ex) when (ex.GetType().Name == "PostgresException")
                    {
                        Assert.Contains("does not exist", ex.Message.ToLower());
                        Console.WriteLine($"\t\t[{apiName}] ✅ PostgresException: {ex.Message}");
                    }
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("PG_DATABASE", originalDb);
            }
        }
    }
}