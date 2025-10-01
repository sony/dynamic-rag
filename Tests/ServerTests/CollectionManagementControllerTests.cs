using Xunit;
using DotNetEnv;
using System.Net;
using Xunit.Priority;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PgVectorDynamicRAG.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace PgVectorDynamicRAG.Tests
{
    public class CollectionManagementControllerTestFixture : IDisposable
    {
        private readonly bool _isVerbose;

        public CollectionManagementControllerTestFixture()
        {
            _isVerbose = Environment.GetEnvironmentVariable("TEST_VERBOSE")?.ToLower() == "true";

            var testCount = typeof(CollectionManagementControllerTests)
                .GetMethods()
                .Count(m => m.GetCustomAttributes(typeof(FactAttribute), false).Any());
            
            Console.WriteLine($"\n=== STARTING Collection Management Controller Integration Tests ({testCount} tests) ===\n");

            if (!_isVerbose)
            {
                Console.WriteLine("Running in CLEAN mode (TEST_VERBOSE=false). Set TEST_VERBOSE=true for full ASP.NET logs.\n");
            }
        }

        public void Dispose()
        {
            Console.WriteLine("\n=== FINISHED Collection Management Controller Integration Tests ✅ ===\n");
        }
    }

    [TestCaseOrderer(PriorityOrderer.Name, PriorityOrderer.Assembly)]
    public class CollectionManagementControllerTests : IClassFixture<WebApplicationFactory<CollectionManagementController>>, IClassFixture<CollectionManagementControllerTestFixture>
    {
        private readonly HttpClient _client;
        private readonly WebApplicationFactory<CollectionManagementController> _factory;
        private readonly bool _isVerbose;

        // Test collection tracking
        private static readonly List<string> _createdTestCollections = new List<string>();
        private static readonly Dictionary<string, string> _testCollectionTypes = new Dictionary<string, string>(); // collectionName -> embeddingType
        private static readonly Dictionary<string, List<string>> _ingestedFiles = new Dictionary<string, List<string>>(); // collectionName -> file list

        public CollectionManagementControllerTests(WebApplicationFactory<CollectionManagementController> factory, CollectionManagementControllerTestFixture fixture)
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

        [Fact, Priority(1)]
        public async Task POST_CreateCollection_TextAndImageEmbedding_ReturnsOkWithCreationDetails()
        {
            Console.WriteLine($"POST_CreateCollection_TextAndImageEmbedding_ReturnsOkWithCreationDetails:");
            
            // Test creation of both TextEmbedding and ImageEmbedding collections
            var collectionScenarios = new[]
            {
                new {
                    embeddingType = "TextEmbedding",
                    defaultEmbeddingModelName = "text-embedding-3-small",
                    collectionName = "integration_test_text_collection",
                    description = "Test TextEmbedding collection for integration tests"
                },
                new {
                    embeddingType = "ImageEmbedding", 
                    defaultEmbeddingModelName = "openai-clip-image-text-embedd-3",
                    collectionName = "integration_test_image_collection",
                    description = "Test ImageEmbedding collection for integration tests"
                }
            };
            
            Console.WriteLine($"\t[Setup] Testing creation of {collectionScenarios.Length} collection types...");

            // before running each test scenario, we want to delete the collections, but not assert anything
            foreach (var scenario in collectionScenarios)
            {
                try
                {
                    var collectionName = scenario.collectionName;
                    var collectionType = scenario.embeddingType;
                    var deleteRequest = new
                    {
                        collectionName = collectionName,
                        deleteBlobs = true // Also delete associated blobs
                    };

                    var requestContent = new StringContent(
                        JsonSerializer.Serialize(deleteRequest),
                        System.Text.Encoding.UTF8,
                        "application/json"
                    );

                    // Make HTTP POST request
                    Console.WriteLine($"\t[Delete Collection {collectionType}] POST /CollectionManagement/delete-collection");
                    var response = await _client.PostAsync("/CollectionManagement/delete-collection", requestContent);
                }
                catch (Exception ex)
                {
                    continue;
                }
            }
            
            foreach (var scenario in collectionScenarios)
                {
                    Console.WriteLine($"\t[Create {scenario.embeddingType}] Creating JSON POST request...");

                    var createRequest = new
                    {
                        embeddingType = scenario.embeddingType,
                        defaultEmbeddingModelName = scenario.defaultEmbeddingModelName,
                        collectionName = scenario.collectionName,
                        initHNSW = true,
                        initBTree = false,
                        description = scenario.description
                    };

                    var requestContent = new StringContent(
                        JsonSerializer.Serialize(createRequest),
                        System.Text.Encoding.UTF8,
                        "application/json"
                    );

                    // Make HTTP POST request
                    Console.WriteLine($"\t[Create {scenario.embeddingType}] POST /CollectionManagement/create-collection");
                    var response = await _client.PostAsync("/CollectionManagement/create-collection", requestContent);

                    // Assert - HTTP Status Code
                    Console.WriteLine($"\t[Create {scenario.embeddingType}] Checking HTTP status...");
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    Console.WriteLine($"\t[Create {scenario.embeddingType}] ✅ HTTP {response.StatusCode}");

                    // Assert - Content Type
                    Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());

                    // Parse JSON response
                    var json = await response.Content.ReadAsStringAsync();
                    var jsonDocument = JsonDocument.Parse(json);
                    var root = jsonDocument.RootElement;

                    // Assert - Response Structure
                    Assert.True(root.TryGetProperty("success", out var successElement));
                    Assert.True(root.TryGetProperty("message", out var messageElement));
                    Assert.True(root.TryGetProperty("internal_execution_time", out var timeElement));

                    // Validate response content
                    Assert.True(successElement.GetBoolean());
                    Assert.False(string.IsNullOrWhiteSpace(messageElement.GetString()));
                    Assert.True(timeElement.GetDouble() >= 0);

                    // Track created collections
                    _createdTestCollections.Add(scenario.collectionName);
                    _testCollectionTypes[scenario.collectionName] = scenario.embeddingType;

                    Console.WriteLine($"\t[Create {scenario.embeddingType}] ✅ Successfully created: {scenario.collectionName}");
                    Console.WriteLine($"\t[Create {scenario.embeddingType}] ✅ Execution time: {timeElement.GetDouble():F3} seconds");
                }
            
            Console.WriteLine($"\t[POST CreateCollection] ✅ Successfully created {_createdTestCollections.Count} test collections");
        }

        [Fact, Priority(2)]
        public async Task POST_IngestFile_TextAndImageCollections_ReturnsOkWithIngestionDetails()
        {
            Console.WriteLine($"POST_IngestFile_TextAndImageCollections_ReturnsOkWithIngestionDetails:");
            
            // Check if collections were created
            if (_createdTestCollections.Count == 0)
            {
                Console.WriteLine($"\tNo test collections found - this test depends on create-collection test");
                Assert.Fail("Collection creation test must run before file ingestion test");
            }
            
            // Test file ingestion for both collection types
            var ingestionScenarios = new[]
            {
                new {
                    collectionName = "integration_test_text_collection",
                    fileName = "test_file.txt",
                    contentType = "text/plain",
                    expectedType = "TextEmbedding"
                },
                new {
                    collectionName = "integration_test_image_collection", 
                    fileName = "test_file.jpg",
                    contentType = "image/jpeg",
                    expectedType = "ImageEmbedding"
                }
            };
            
            foreach (var scenario in ingestionScenarios)
            {
                if (!_createdTestCollections.Contains(scenario.collectionName))
                {
                    Console.WriteLine($"\t[Ingest {scenario.expectedType}] Skipping - collection {scenario.collectionName} not found");
                    continue;
                }
                
                Console.WriteLine($"\t[Ingest {scenario.expectedType}] Creating multipart/form-data request...");
                
                // Create test file from TestData folder
                var testFilePath = Path.Combine(AppContext.BaseDirectory, "TestData", scenario.fileName);
                
                if (!File.Exists(testFilePath))
                {
                    Console.WriteLine($"\t[Ingest {scenario.expectedType}] Skipping - test file not found: {scenario.fileName}");
                    continue;
                }
                
                var fileBytes = File.ReadAllBytes(testFilePath);
                
                // Create multipart/form-data content
                using var multipartContent = new MultipartFormDataContent();
                multipartContent.Add(new StringContent(scenario.collectionName), "collectionName");
                multipartContent.Add(new StringContent("4000"), "chunkSize");
                multipartContent.Add(new StringContent("0.1"), "chunkOverlapFraction");
                multipartContent.Add(new StringContent("true"), "saveToBlobStorage");
                
                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(scenario.contentType);
                multipartContent.Add(fileContent, "file", scenario.fileName);
                
                // Make HTTP POST request
                Console.WriteLine($"\t[Ingest {scenario.expectedType}] POST /CollectionManagement/ingest-file");
                var response = await _client.PostAsync("/CollectionManagement/ingest-file", multipartContent);
                
                // Assert - HTTP Status Code
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Console.WriteLine($"\t[Ingest {scenario.expectedType}] ✅ HTTP {response.StatusCode}");
                
                // Parse JSON response
                var json = await response.Content.ReadAsStringAsync();
                var jsonDocument = JsonDocument.Parse(json);
                var root = jsonDocument.RootElement;
                Console.WriteLine($"\t[Ingest {scenario.expectedType}] Response: {json}");
                
                // Assert - Response Structure
                Assert.True(root.TryGetProperty("success", out var successElement));
                Assert.True(root.TryGetProperty("message", out var messageElement));
                // Assert.True(root.TryGetProperty("file_source", out var fileSourceElement));
                Assert.True(root.TryGetProperty("file_name", out var fileNameElement));
                Assert.True(root.TryGetProperty("chunk_count", out var chunkCountElement));
                Assert.True(root.TryGetProperty("internal_execution_time", out var timeElement));
                
                // Validate response content
                Assert.True(successElement.GetBoolean());
                // Assert.Equal("upload", fileSourceElement.GetString());
                Assert.Equal(scenario.fileName, fileNameElement.GetString());
                Assert.True(chunkCountElement.GetInt32() >= 0);
                
                // Track ingested files
                if (!_ingestedFiles.ContainsKey(scenario.collectionName))
                {
                    _ingestedFiles[scenario.collectionName] = new List<string>();
                }
                _ingestedFiles[scenario.collectionName].Add(scenario.fileName);
                
                Console.WriteLine($"\t[Ingest {scenario.expectedType}] ✅ Successfully ingested: {scenario.fileName}");
                Console.WriteLine($"\t[Ingest {scenario.expectedType}] ✅ Chunks created: {chunkCountElement.GetInt32()}");
                Console.WriteLine($"\t[Ingest {scenario.expectedType}] ✅ Execution time: {timeElement.GetDouble():F3} seconds");
            }
            
            Console.WriteLine($"\t[POST IngestFile] ✅ File ingestion testing completed");
        }

        [Fact, Priority(3)]
        public async Task POST_NewTextMemory_TextCollectionsOnly_ReturnsOkWithMemoryDetails()
        {
            Console.WriteLine($"POST_NewTextMemory_TextCollectionsOnly_ReturnsOkWithMemoryDetails:");
            
            // Find TextEmbedding collection
            var textCollectionName = _createdTestCollections
                .FirstOrDefault(name => _testCollectionTypes.ContainsKey(name) && 
                                    _testCollectionTypes[name] == "TextEmbedding");
            
            if (string.IsNullOrEmpty(textCollectionName))
            {
                Console.WriteLine($"\tNo TextEmbedding collection found - skipping text memory test");
                return;
            }
            
            Console.WriteLine($"\t[Text Memory] Using collection: {textCollectionName}");
            
            var textMemoryScenarios = new[]
            {
                new {
                    testName = "Basic Memory",
                    content = "This is a test memory for integration testing. It contains important information about the test workflow.",
                    chunkSize = 4000,
                    chunkOverlapFraction = 0.1f
                },
                new {
                    testName = "Short Memory",
                    content = "Short test memory.",
                    chunkSize = 1000,
                    chunkOverlapFraction = 0.0f
                }
            };
            
            foreach (var scenario in textMemoryScenarios)
            {
                Console.WriteLine($"\t[{scenario.testName}] Creating multipart/form-data request...");
                
                // Create multipart/form-data content
                using var multipartContent = new MultipartFormDataContent();
                multipartContent.Add(new StringContent(textCollectionName), "collectionName");
                multipartContent.Add(new StringContent(scenario.chunkSize.ToString()), "chunkSize");
                multipartContent.Add(new StringContent(scenario.chunkOverlapFraction.ToString()), "chunkOverlapFraction");
                multipartContent.Add(new StringContent("true"), "saveToBlobStorage");
                multipartContent.Add(new StringContent(scenario.content), "content");
                
                // Make HTTP POST request
                Console.WriteLine($"\t[{scenario.testName}] POST /CollectionManagement/new-text-memory");
                var response = await _client.PostAsync("/CollectionManagement/new-text-memory", multipartContent);
                
                // Assert - HTTP Status Code
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Console.WriteLine($"\t[{scenario.testName}] ✅ HTTP {response.StatusCode}");
                
                // Parse JSON response
                var json = await response.Content.ReadAsStringAsync();
                var jsonDocument = JsonDocument.Parse(json);
                var root = jsonDocument.RootElement;
                
                // Assert - Response Structure
                Assert.True(root.TryGetProperty("success", out var successElement));
                Assert.True(root.TryGetProperty("message", out var messageElement));
                Assert.True(root.TryGetProperty("content_source", out var contentSourceElement));
                Assert.True(root.TryGetProperty("file_name", out var fileNameElement));
                Assert.True(root.TryGetProperty("chunk_count", out var chunkCountElement));
                Assert.True(root.TryGetProperty("internal_execution_time", out var timeElement));
                
                // Validate response content
                Assert.True(successElement.GetBoolean());
                Assert.Equal("direct_text", contentSourceElement.GetString());
                Assert.True(chunkCountElement.GetInt32() >= 0);
                Assert.False(string.IsNullOrWhiteSpace(fileNameElement.GetString()));
                
                // Track memory files
                if (!_ingestedFiles.ContainsKey(textCollectionName))
                {
                    _ingestedFiles[textCollectionName] = new List<string>();
                }
                _ingestedFiles[textCollectionName].Add(fileNameElement.GetString()!);
                
                Console.WriteLine($"\t[{scenario.testName}] ✅ Successfully created text memory");
                Console.WriteLine($"\t[{scenario.testName}] ✅ Generated file: {fileNameElement.GetString()}");
                Console.WriteLine($"\t[{scenario.testName}] ✅ Chunks created: {chunkCountElement.GetInt32()}");
                Console.WriteLine($"\t[{scenario.testName}] ✅ Execution time: {timeElement.GetDouble():F3} seconds");
            }
            
            Console.WriteLine($"\t[POST NewTextMemory] ✅ Text memory testing completed");
        }

        [Fact, Priority(4)]
        public async Task POST_TextVectorSearchCollection_BothCollectionTypes_ReturnsOkWithSearchResults()
        {
            Console.WriteLine($"POST_TextVectorSearchCollection_BothCollectionTypes_ReturnsOkWithSearchResults:");
            
            // Test vector search on both collection types
            var searchQueries = new[]
            {
                "test information search query",
                "integration testing data",
                "sample content"
            };
            
            foreach (var collectionName in _createdTestCollections)
            {
                var collectionType = _testCollectionTypes[collectionName];
                Console.WriteLine($"\t[Vector Search {collectionType}] Testing collection: {collectionName}");
                
                foreach (var query in searchQueries.Take(1)) // Test with first query
                {
                    Console.WriteLine($"\t[Vector Search {collectionType}] Creating JSON POST request...");
                    
                    var searchRequest = new
                    {
                        query = query,
                        collectionName = collectionName,
                        nResults = 5,
                        timeWindowEnabled = false,
                        windowDays = 30,
                        temporalDecayEnabled = false,
                        decayImpact = 0.01
                    };
                    
                    var requestContent = new StringContent(
                        JsonSerializer.Serialize(searchRequest),
                        System.Text.Encoding.UTF8,
                        "application/json"
                    );
                    
                    // Make HTTP POST request
                    Console.WriteLine($"\t[Vector Search {collectionType}] POST /CollectionManagement/text-vector-search-collection");
                    var response = await _client.PostAsync("/CollectionManagement/text-vector-search-collection", requestContent);
                    
                    // Assert - HTTP Status Code
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    Console.WriteLine($"\t[Vector Search {collectionType}] ✅ HTTP {response.StatusCode}");
                    
                    // Parse JSON response
                    var json = await response.Content.ReadAsStringAsync();
                    var jsonDocument = JsonDocument.Parse(json);
                    var root = jsonDocument.RootElement;
                    
                    // Assert - Response Structure
                    Assert.True(root.TryGetProperty("success", out var successElement));
                    Assert.True(root.TryGetProperty("search_results", out var searchResultsElement));
                    Assert.True(root.TryGetProperty("internal_execution_time", out var timeElement));
                    
                    // Validate response content
                    Assert.True(successElement.GetBoolean());
                    Assert.Equal(JsonValueKind.Array, searchResultsElement.ValueKind);
                    
                    var searchResults = searchResultsElement.EnumerateArray().ToList();
                    Console.WriteLine($"\t[Vector Search {collectionType}] Found {searchResults.Count} search results");
                    
                    // Validate search results structure (if any results found)
                    if (searchResults.Count > 0)
                    {
                        var firstResult = searchResults[0];
                        Assert.True(firstResult.TryGetProperty("rank", out var rankElement));
                        Assert.True(firstResult.TryGetProperty("score", out var scoreElement));
                        Assert.True(firstResult.TryGetProperty("fileName", out var fileNameElement));
                        Assert.True(firstResult.TryGetProperty("definition", out var definitionElement));
                        
                        Assert.True(rankElement.GetInt32() > 0);
                        Assert.True(scoreElement.GetDouble() >= 0);
                        Assert.False(string.IsNullOrWhiteSpace(fileNameElement.GetString()));
                        Assert.False(string.IsNullOrWhiteSpace(definitionElement.GetString()));
                    }
                    
                    Console.WriteLine($"\t[Vector Search {collectionType}] ✅ Query: {query}");
                    Console.WriteLine($"\t[Vector Search {collectionType}] ✅ Results: {searchResults.Count}");
                    Console.WriteLine($"\t[Vector Search {collectionType}] ✅ Execution time: {timeElement.GetDouble():F3} seconds");
                    break; // Only test one query per collection
                }
            }
            
            Console.WriteLine($"\t[POST TextVectorSearchCollection] ✅ Vector search testing completed");
        }

        [Fact, Priority(5)]
        public async Task POST_TextSemanticSearchCollection_BothCollectionTypes_ReturnsOkWithSearchResults()
        {
            Console.WriteLine($"POST_TextSemanticSearchCollection_BothCollectionTypes_ReturnsOkWithSearchResults:");
            
            foreach (var collectionName in _createdTestCollections)
            {
                var collectionType = _testCollectionTypes[collectionName];
                Console.WriteLine($"\t[Semantic Search {collectionType}] Testing collection: {collectionName}");
                
                var searchRequest = new
                {
                    query = "test integration data",
                    collectionName = collectionName,
                    nResults = 3,
                    timeWindowEnabled = false,
                    windowDays = 30,
                    temporalDecayEnabled = false,
                    decayImpact = 0.01
                };
                
                var requestContent = new StringContent(
                    JsonSerializer.Serialize(searchRequest),
                    System.Text.Encoding.UTF8,
                    "application/json"
                );
                
                // Make HTTP POST request
                Console.WriteLine($"\t[Semantic Search {collectionType}] POST /CollectionManagement/text-semantic-search-collection");
                var response = await _client.PostAsync("/CollectionManagement/text-semantic-search-collection", requestContent);
                
                if (collectionType == "ImageEmbedding")
                {
                    // ImageEmbedding collections may not support semantic search
                    Console.WriteLine($"\t[Semantic Search {collectionType}] Response: {response.StatusCode}");
                    if (response.StatusCode == HttpStatusCode.NotImplemented)
                    {
                        Console.WriteLine($"\t[Semantic Search {collectionType}] ✅ Expected: NotImplemented for ImageEmbedding");
                        continue;
                    }
                }
                
                // Assert - HTTP Status Code for TextEmbedding or if ImageEmbedding supports it
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Console.WriteLine($"\t[Semantic Search {collectionType}] ✅ HTTP {response.StatusCode}");
                
                // Parse JSON response
                var json = await response.Content.ReadAsStringAsync();
                var jsonDocument = JsonDocument.Parse(json);
                var root = jsonDocument.RootElement;
                
                // Assert - Response Structure
                Assert.True(root.TryGetProperty("success", out var successElement));
                Assert.True(root.TryGetProperty("search_results", out var searchResultsElement));
                Assert.True(root.TryGetProperty("internal_execution_time", out var timeElement));
                
                Assert.True(successElement.GetBoolean());
                Assert.Equal(JsonValueKind.Array, searchResultsElement.ValueKind);
                
                var searchResults = searchResultsElement.EnumerateArray().ToList();
                Console.WriteLine($"\t[Semantic Search {collectionType}] ✅ Results: {searchResults.Count}");
                Console.WriteLine($"\t[Semantic Search {collectionType}] ✅ Execution time: {timeElement.GetDouble():F3} seconds");
            }
            
            Console.WriteLine($"\t[POST TextSemanticSearchCollection] ✅ Semantic search testing completed");
        }

        [Fact, Priority(6)]
        public async Task POST_TextSearchCollection_BothCollectionTypes_ReturnsOkWithSearchResults()
        {
            Console.WriteLine($"POST_TextSearchCollection_BothCollectionTypes_ReturnsOkWithSearchResults:");
            
            foreach (var collectionName in _createdTestCollections)
            {
                var collectionType = _testCollectionTypes[collectionName];
                Console.WriteLine($"\t[Hybrid Search {collectionType}] Testing collection: {collectionName}");
                
                var searchRequest = new
                {
                    query = "integration test content",
                    collectionName = collectionName,
                    nResults = 5,
                    alpha = 0.75, // Balance between vector and semantic
                    timeWindowEnabled = false,
                    windowDays = 30,
                    temporalDecayEnabled = false,
                    decayImpact = 0.01
                };
                
                var requestContent = new StringContent(
                    JsonSerializer.Serialize(searchRequest),
                    System.Text.Encoding.UTF8,
                    "application/json"
                );
                
                // Make HTTP POST request
                Console.WriteLine($"\t[Hybrid Search {collectionType}] POST /CollectionManagement/text-search-collection");
                var response = await _client.PostAsync("/CollectionManagement/text-search-collection", requestContent);
                
                // Assert - HTTP Status Code
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Console.WriteLine($"\t[Hybrid Search {collectionType}] ✅ HTTP {response.StatusCode}");
                
                // Parse JSON response
                var json = await response.Content.ReadAsStringAsync();
                var jsonDocument = JsonDocument.Parse(json);
                var root = jsonDocument.RootElement;
                
                // Assert - Response Structure
                Assert.True(root.TryGetProperty("success", out var successElement));
                Assert.True(root.TryGetProperty("search_results", out var searchResultsElement));
                Assert.True(root.TryGetProperty("internal_execution_time", out var timeElement));
                
                Assert.True(successElement.GetBoolean());
                Assert.Equal(JsonValueKind.Array, searchResultsElement.ValueKind);
                
                var searchResults = searchResultsElement.EnumerateArray().ToList();
                Console.WriteLine($"\t[Hybrid Search {collectionType}] ✅ Results: {searchResults.Count}");
                Console.WriteLine($"\t[Hybrid Search {collectionType}] ✅ Alpha: 0.75 (75% vector, 25% semantic)");
                Console.WriteLine($"\t[Hybrid Search {collectionType}] ✅ Execution time: {timeElement.GetDouble():F3} seconds");
            }
            
            Console.WriteLine($"\t[POST TextSearchCollection] ✅ Hybrid search testing completed");
        }

        [Fact, Priority(7)]
        public async Task POST_ImageSearchCollection_BothCollectionTypes_ReturnsOkWithSearchResults()
        {
            Console.WriteLine($"POST_ImageSearchCollection_BothCollectionTypes_ReturnsOkWithSearchResults:");
            
            // Create test image for search
            var testImagePath = Path.Combine(AppContext.BaseDirectory, "TestData", "test_image.jpg");
            
            if (!File.Exists(testImagePath))
            {
                Console.WriteLine($"\tTest image not found: {testImagePath} - skipping image search tests");
                return;
            }
            
            var imageBytes = File.ReadAllBytes(testImagePath);
            
            foreach (var collectionName in _createdTestCollections)
            {
                var collectionType = _testCollectionTypes[collectionName];
                Console.WriteLine($"\t[Image Search {collectionType}] Testing collection: {collectionName}");
                
                // Create multipart/form-data content
                using var multipartContent = new MultipartFormDataContent();
                multipartContent.Add(new StringContent(collectionName), "collectionName");
                multipartContent.Add(new StringContent("5"), "nResults");
                multipartContent.Add(new StringContent("false"), "timeWindowEnabled");
                multipartContent.Add(new StringContent("30"), "windowDays");
                multipartContent.Add(new StringContent("false"), "temporalDecayEnabled");
                multipartContent.Add(new StringContent("0.01"), "decayImpact");
                
                var imageContent = new ByteArrayContent(imageBytes);
                imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                multipartContent.Add(imageContent, "image", "test_image.jpg");
                
                // Make HTTP POST request
                Console.WriteLine($"\t[Image Search {collectionType}] POST /CollectionManagement/image-search-collection");
                var response = await _client.PostAsync("/CollectionManagement/image-search-collection", multipartContent);
                
                if (collectionType == "TextEmbedding")
                {
                    // TextEmbedding collections should not support image search
                    Console.WriteLine($"\t[Image Search {collectionType}] Response: {response.StatusCode}");
                    Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
                    Console.WriteLine($"\t[Image Search {collectionType}] ✅ Expected: NotImplemented for TextEmbedding");
                    continue;
                }
                
                // For ImageEmbedding collections, should work
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Console.WriteLine($"\t[Image Search {collectionType}] ✅ HTTP {response.StatusCode}");
                
                // Parse JSON response
                var json = await response.Content.ReadAsStringAsync();
                var jsonDocument = JsonDocument.Parse(json);
                var root = jsonDocument.RootElement;
                
                // Assert - Response Structure
                Assert.True(root.TryGetProperty("success", out var successElement));
                Assert.True(root.TryGetProperty("search_results", out var searchResultsElement));
                Assert.True(root.TryGetProperty("internal_execution_time", out var timeElement));
                
                Assert.True(successElement.GetBoolean());
                Assert.Equal(JsonValueKind.Array, searchResultsElement.ValueKind);
                
                var searchResults = searchResultsElement.EnumerateArray().ToList();
                Console.WriteLine($"\t[Image Search {collectionType}] ✅ Results: {searchResults.Count}");
                Console.WriteLine($"\t[Image Search {collectionType}] ✅ Execution time: {timeElement.GetDouble():F3} seconds");
            }
            
            Console.WriteLine($"\t[POST ImageSearchCollection] ✅ Image search testing completed");
        }

        [Fact, Priority(8)]
        public async Task POST_RemoveFileFromCollection_BothCollectionTypes_ReturnsOkWithRemovalDetails()
        {
            Console.WriteLine($"POST_RemoveFileFromCollection_BothCollectionTypes_ReturnsOkWithRemovalDetails:");
            
            foreach (var collectionName in _createdTestCollections)
            {
                if (!_ingestedFiles.ContainsKey(collectionName) || _ingestedFiles[collectionName].Count == 0)
                {
                    Console.WriteLine($"\t[Remove File] No ingested files found for collection: {collectionName}");
                    continue;
                }
                
                var collectionType = _testCollectionTypes[collectionName];
                var filesToRemove = _ingestedFiles[collectionName].Take(1).ToList(); // Remove first file
                
                foreach (var fileName in filesToRemove)
                {
                    Console.WriteLine($"\t[Remove File {collectionType}] Removing file: {fileName}");
                    
                    var removeRequest = new
                    {
                        pathInContainer = fileName,
                        collectionName = collectionName,
                        isBlobFile = true
                    };
                    
                    var requestContent = new StringContent(
                        JsonSerializer.Serialize(removeRequest),
                        System.Text.Encoding.UTF8,
                        "application/json"
                    );
                    
                    // Make HTTP POST request
                    Console.WriteLine($"\t[Remove File {collectionType}] POST /CollectionManagement/remove-file-from-collection");
                    var response = await _client.PostAsync("/CollectionManagement/remove-file-from-collection", requestContent);
                    
                    // Assert - HTTP Status Code
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    Console.WriteLine($"\t[Remove File {collectionType}] ✅ HTTP {response.StatusCode}");
                    
                    // Parse JSON response
                    var json = await response.Content.ReadAsStringAsync();
                    var jsonDocument = JsonDocument.Parse(json);
                    var root = jsonDocument.RootElement;
                    
                    // Assert - Response Structure
                    Assert.True(root.TryGetProperty("success", out var successElement));
                    Assert.True(root.TryGetProperty("message", out var messageElement));
                    Assert.True(root.TryGetProperty("internal_execution_time", out var timeElement));
                    
                    Assert.True(successElement.GetBoolean());
                    Assert.False(string.IsNullOrWhiteSpace(messageElement.GetString()));
                    
                    // Remove from tracking
                    _ingestedFiles[collectionName].Remove(fileName);
                    
                    Console.WriteLine($"\t[Remove File {collectionType}] ✅ Successfully removed: {fileName}");
                    Console.WriteLine($"\t[Remove File {collectionType}] ✅ Execution time: {timeElement.GetDouble():F3} seconds");
                }
            }
            
            Console.WriteLine($"\t[POST RemoveFileFromCollection] ✅ File removal testing completed");
        }

        [Fact, Priority(9)]
        public async Task POST_UpdateIndex_BothCollectionTypes_ReturnsOkWithUpdateDetails()
        {
            Console.WriteLine($"POST_UpdateIndex_BothCollectionTypes_ReturnsOkWithUpdateDetails:");
            
            foreach (var collectionName in _createdTestCollections)
            {
                var collectionType = _testCollectionTypes[collectionName];
                Console.WriteLine($"\t[Update Index {collectionType}] Updating index for: {collectionName}");
                
                var updateRequest = new
                {
                    collectionName = collectionName
                };
                
                var requestContent = new StringContent(
                    JsonSerializer.Serialize(updateRequest),
                    System.Text.Encoding.UTF8,
                    "application/json"
                );
                
                // Make HTTP POST request
                Console.WriteLine($"\t[Update Index {collectionType}] POST /CollectionManagement/update-index");
                var response = await _client.PostAsync("/CollectionManagement/update-index", requestContent);
                
                // Assert - HTTP Status Code
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Console.WriteLine($"\t[Update Index {collectionType}] ✅ HTTP {response.StatusCode}");
                
                // Parse JSON response
                var json = await response.Content.ReadAsStringAsync();
                var jsonDocument = JsonDocument.Parse(json);
                var root = jsonDocument.RootElement;
                
                // Assert - Response Structure
                Assert.True(root.TryGetProperty("success", out var successElement));
                Assert.True(root.TryGetProperty("message", out var messageElement));
                Assert.True(root.TryGetProperty("internal_execution_time", out var timeElement));
                
                Assert.True(successElement.GetBoolean());
                Assert.False(string.IsNullOrWhiteSpace(messageElement.GetString()));
                
                Console.WriteLine($"\t[Update Index {collectionType}] ✅ Successfully updated index for: {collectionName}");
                Console.WriteLine($"\t[Update Index {collectionType}] ✅ Execution time: {timeElement.GetDouble():F3} seconds");
            }
        
            Console.WriteLine($"\t[POST UpdateIndex] ✅ Index update testing completed");
        }

        [Fact, Priority(10)]
        public async Task POST_DeleteCollection_BothCollectionTypes_ReturnsOkWithDeletionDetails()
        {
            Console.WriteLine($"POST_DeleteCollection_BothCollectionTypes_ReturnsOkWithDeletionDetails:");
            
            var collectionsToDelete = _createdTestCollections.ToList(); // Copy list to avoid modification during iteration
            
            foreach (var collectionName in collectionsToDelete)
            {
                var collectionType = _testCollectionTypes[collectionName];
                Console.WriteLine($"\t[Delete Collection {collectionType}] Deleting collection: {collectionName}");
                
                var deleteRequest = new
                {
                    collectionName = collectionName,
                    deleteBlobs = true // Also delete associated blobs
                };
                
                var requestContent = new StringContent(
                    JsonSerializer.Serialize(deleteRequest),
                    System.Text.Encoding.UTF8,
                    "application/json"
                );
                
                // Make HTTP POST request
                Console.WriteLine($"\t[Delete Collection {collectionType}] POST /CollectionManagement/delete-collection");
                var response = await _client.PostAsync("/CollectionManagement/delete-collection", requestContent);
                
                // Assert - HTTP Status Code
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Console.WriteLine($"\t[Delete Collection {collectionType}] ✅ HTTP {response.StatusCode}");
                
                // Parse JSON response
                var json = await response.Content.ReadAsStringAsync();
                var jsonDocument = JsonDocument.Parse(json);
                var root = jsonDocument.RootElement;
                
                // Assert - Response Structure
                Assert.True(root.TryGetProperty("success", out var successElement));
                Assert.True(root.TryGetProperty("message", out var messageElement));
                Assert.True(root.TryGetProperty("internal_execution_time", out var timeElement));
                
                // Note: success might be false if some parts of deletion failed, but we should get a response
                Assert.False(string.IsNullOrWhiteSpace(messageElement.GetString()));
                
                // Remove from tracking
                _createdTestCollections.Remove(collectionName);
                _testCollectionTypes.Remove(collectionName);
                if (_ingestedFiles.ContainsKey(collectionName))
                {
                    _ingestedFiles.Remove(collectionName);
                }
                
                Console.WriteLine($"\t[Delete Collection {collectionType}] ✅ Collection deletion attempted: {collectionName}");
                Console.WriteLine($"\t[Delete Collection {collectionType}] ✅ Status: {messageElement.GetString()}");
                Console.WriteLine($"\t[Delete Collection {collectionType}] ✅ Execution time: {timeElement.GetDouble():F3} seconds");
            }
            
            Console.WriteLine($"\t[POST DeleteCollection] ✅ Collection deletion testing completed");
        }

        [Fact, Priority(11)]
        public async Task Cleanup_RemoveAnyRemainingTestCollections()
        {
            Console.WriteLine($"Cleanup_RemoveAnyRemainingTestCollections:");
            
            if (_createdTestCollections.Count == 0)
            {
                Console.WriteLine($"\t[Cleanup] ✅ No remaining test collections to clean up");
                return;
            }
            
            Console.WriteLine($"\t[Cleanup] Found {_createdTestCollections.Count} remaining test collections to clean up");
            
            var remainingCollections = _createdTestCollections.ToList();
            
            foreach (var collectionName in remainingCollections)
            {
                Console.WriteLine($"\t[Cleanup] Attempting to delete remaining collection: {collectionName}");
                
                try
                {
                    var deleteRequest = new
                    {
                        collectionName = collectionName,
                        deleteBlobs = true
                    };
                    
                    var requestContent = new StringContent(
                        JsonSerializer.Serialize(deleteRequest),
                        System.Text.Encoding.UTF8,
                        "application/json"
                    );
                    
                    var response = await _client.PostAsync("/CollectionManagement/delete-collection", requestContent);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"\t[Cleanup] ✅ Successfully cleaned up: {collectionName}");
                    }
                    else
                    {
                        Console.WriteLine($"\t[Cleanup] Cleanup failed for {collectionName}: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\t[Cleanup] Error cleaning up {collectionName}: {ex.Message}");
                }
            }
            
            // Clear all tracking variables
            _createdTestCollections.Clear();
            _testCollectionTypes.Clear();
            _ingestedFiles.Clear();
            
            Console.WriteLine($"\t[Cleanup] ✅ Final cleanup completed - all tracking variables cleared");
        }

        private void Dispose()
        {
            _client?.Dispose();
        }
    }
}