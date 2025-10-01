using Microsoft.SemanticKernel;
using Microsoft.Extensions.VectorData;
using Npgsql;
using PgVectorDynamicRAG.Models;
using PgVectorDynamicRAG.Factories;
using PgVectorDynamicRAG.Data;
using System.Reflection;
using ModelContextProtocol.Server;
using System.ComponentModel;

#pragma warning disable SKEXP0001

namespace PgVectorDynamicRAG.Services
{
    /// <summary>
    /// Responsible for managing collections, delegating embedding operations to specialized services,
    /// and providing a unified interface for collection operations.
    /// </summary>
    [McpServerToolType]
    public class CollectionManagementService
    {
        private readonly Kernel _kernel;
        private readonly ILogger<CollectionManagementService> _logger;
        private readonly IPostgresConnectionFactory _connectionFactory;
        private readonly SchemaManagementService _schemaManagementService;
        private readonly AbstractStorageService _storageManagementService;
        private readonly EmbeddingServiceFactory _embeddingServiceFactory;
        
        /// <summary>
        /// Constructor for the collection management service.
        /// </summary>
        /// <param name="kernel">The Semantic Kernel instance</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="connectionFactory">Postgres connection factory</param>
        /// <param name="schemaManagementService">Schema management service</param>
        /// <param name="storageManagementService">Storage management service</param>
        /// <param name="embeddingServiceFactory">Factory for creating embedding services</param>
        
        public CollectionManagementService(
            Kernel kernel,
            ILogger<CollectionManagementService> logger,
            IPostgresConnectionFactory connectionFactory,
            SchemaManagementService schemaManagementService,
            AbstractStorageService storageManagementService,
            EmbeddingServiceFactory embeddingServiceFactory)
        {
            _kernel = kernel;
            _logger = logger;
            _connectionFactory = connectionFactory;
            _schemaManagementService = schemaManagementService;
            _storageManagementService = storageManagementService;
            _embeddingServiceFactory = embeddingServiceFactory;
        }

        /// <summary>
        /// Create a new collection in the vector database.
        /// </summary>
        /// <param name="embeddingType">Type of embedding (e.g., "TextEmbedding" or "ImageEmbedding")</param>
        /// <param name="defaultEmbeddingModelName">Default embedding model name</param>
        /// <param name="collectionName">Name of the collection to create</param>
        /// <param name="description">Description of the collection</param>
        /// <param name="initHNSW">Whether to initialize HNSW indexing</param>
        /// <param name="initBTree">Whether to initialize BTree indexing</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary with operation results</returns>
        [McpServerTool, Description("Create a new collection in the vector database.")]
        public async Task<Dictionary<string, object>> CreateCollection(
            string embeddingType, // will either be "TextEmbedding" or "ImageEmbedding"
            string defaultEmbeddingModelName,
            string collectionName,
            string? description = null,
            bool initHNSW = true,
            bool initBTree = false,
            CancellationToken cancellationToken = default)
        {
            var resultsDictionary = new Dictionary<string, object>
            {
                { "success", (bool?)null },
                { "message", (string?)null }
            };
            
            try
            {
                _logger.LogInformation("Creating collection {CollectionName} with default embedding model {ModelName}", collectionName, defaultEmbeddingModelName);
                
                // Get the vector store from the kernel's DI container
                var vectorStore = _kernel.GetRequiredService<VectorStore>();
                
                if (vectorStore == null)
                {
                    throw new InvalidOperationException("CreateCollection error: vector store is not configured in DI.");
                }
                
                // Check if the collection exists
                bool collectionExists = await _schemaManagementService.CheckCollectionExists(collectionName, cancellationToken);
                if (collectionExists)
                {
                    throw new InvalidOperationException($"Collection with name {collectionName} already exists.");
                }
                
                // Get the model details from the ModelRegistry based on embeddingType
                if (!ModelDetails.ModelRegistry.TryGetValue(embeddingType, out var modelDetailsDict))
                {
                    throw new InvalidOperationException($"Embedding type '{embeddingType}' not found in ModelRegistry.");
                }
                
                if (!modelDetailsDict.TryGetValue(defaultEmbeddingModelName, out var modelDetails))
                {
                    throw new InvalidOperationException($"Embedding model '{defaultEmbeddingModelName}' not found in ModelRegistry for type {embeddingType}.");
                }
                
                // Get the embedding service ID and dimension
                (string embeddingServiceId, int embeddingDimension) = await _schemaManagementService.ValidateEmbeddingModel(defaultEmbeddingModelName);
                
                // Extract model details
                int dimension = modelDetails.Dimension;
                Type glossaryType = modelDetails.GlossaryModel;
                string modality = modelDetails.Modality;
                string defaultDistanceMetric = modelDetails.DefaultSimilarityAlgo;
                
                _logger.LogInformation($"Glossary Type: {glossaryType}");
                
                // Create the collection using reflection to handle the generic type
                MethodInfo? getCollectionMethod = vectorStore.GetType().GetMethod("GetCollection");
                if (getCollectionMethod == null)
                {
                    throw new InvalidOperationException("GetCollection method not found on IVectorStore.");
                }
                _logger.LogInformation($"getCollectionMethod: {getCollectionMethod}");
                
                MethodInfo genericGetCollectionMethod = getCollectionMethod.MakeGenericMethod(typeof(Guid), glossaryType);
                _logger.LogInformation($"genericGetCollectionMethod: {genericGetCollectionMethod}");
                
                var collection = genericGetCollectionMethod.Invoke(vectorStore, new object[] { collectionName, Type.Missing });
                if (collection == null)
                {
                    throw new InvalidOperationException("Failed to create collection instance.");
                }
                _logger.LogInformation($"collection: {collection}");
                
                MethodInfo? createMethod = collection.GetType().GetMethod("EnsureCollectionExistsAsync");
                if (createMethod == null)
                {
                    throw new InvalidOperationException("EnsureCollectionExistsAsync method not found on collection.");
                }
                _logger.LogInformation($"createMethod: {createMethod}");
                
                await (Task)createMethod.Invoke(collection, new object[] { default(CancellationToken) });
                
                // If we get here, the collection was created successfully
                
                // Insert the collection type into the collection_directory table
                var insertEntrySql = @"
                    INSERT INTO collection_directory (collection_name, collection_type, default_embedding_service_id, default_distance_metric, description)
                    VALUES (@collectionName, @embeddingType, @defaultEmbeddingServiceId, @defaultDistanceMetric, @description);
                ";

                // can add additional HNSW parameters like this: CREATE INDEX ON items USING hnsw (embedding vector_l2_ops) WITH (m = 16, ef_construction = 64);
                // https://github.com/pgvector/pgvector?tab=readme-ov-file#hnsw
                // var initHNSWSql = $@"
                //     CREATE INDEX IF NOT EXISTS hnsw_index
                //     ON {collectionName} USING hnsw(""DefinitionEmbedding"" vector_cosine_ops);
                // ";

                // var initBTreeSql = $@"
                //     CREATE INDEX IF NOT EXISTS btree_index
                //     ON {collectionName} USING btree(""EmbeddingServiceId"");
                // ";
                var initIndexSql = "";
                if (embeddingDimension > 2000)
                {
                    // initIndexSql = $@"
                    //     CREATE INDEX IF NOT EXISTS ivf_index_{collectionName}
                    //     ON ""{collectionName}"" USING ivfflat(""DefinitionEmbedding"" vector_cosine_ops)
                    //     WITH (lists = 100);
                    // ";
                    initHNSW = false;
                }
                else
                {
                    initIndexSql = $@"
                        CREATE INDEX IF NOT EXISTS hnsw_index_{collectionName}
                        ON ""{collectionName}"" USING hnsw(""DefinitionEmbedding"" vector_cosine_ops);
                    ";
                }

                var initBTreeSql = $@"
                    CREATE INDEX IF NOT EXISTS btree_index_{collectionName}
                    ON ""{collectionName}"" USING btree(""EmbeddingServiceId"");
                ";

                await using (var conn = _connectionFactory.CreateConnection())
                {
                    await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                    await using (var cmd = new NpgsqlCommand(insertEntrySql, conn))
                    {
                        // Add parameters to avoid SQL injection and ensure type safety.
                        cmd.Parameters.AddWithValue("collectionName", collectionName);
                        cmd.Parameters.AddWithValue("embeddingType", embeddingType);
                        cmd.Parameters.AddWithValue("defaultEmbeddingServiceId", embeddingServiceId);
                        cmd.Parameters.AddWithValue("defaultDistanceMetric", modelDetails.DefaultSimilarityAlgo);
                        cmd.Parameters.AddWithValue("description", description ?? (object)DBNull.Value);

                        // ExecuteNonQueryAsync is used because this command does not return rows.
                        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                    if (initHNSW)
                    {
                        Console.WriteLine($"Creating HNSW index for collection {collectionName}");
                        // await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                        await using (var hnswCmd = new NpgsqlCommand(initIndexSql, conn))
                        {
                            // Add parameters to avoid SQL injection and ensure type safety.
                            // hnswCmd.Parameters.AddWithValue("collectionName", collectionName);

                            // ExecuteNonQueryAsync is used because this command does not return rows.
                            await hnswCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }
                    if (initBTree) // experimental - will construct an additional index on the service_id in case multiple embeddings of same dim from different models
                    {
                        // await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                        await using (var btreeCmd = new NpgsqlCommand(initBTreeSql, conn))
                        {
                            // Add parameters to avoid SQL injection and ensure type safety.
                            // hnswCmd.Parameters.AddWithValue("collectionName", collectionName);

                            // ExecuteNonQueryAsync is used because this command does not return rows.
                            await btreeCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }
                }

                resultsDictionary["success"] = true;
                resultsDictionary["message"] = $"Successfully created new collection";
                return resultsDictionary;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while creating a the new collection.");
                // resultsDictionary["success"] = false;
                // resultsDictionary["message"] = $"CreateNewCollection error: {ex.Message}";
                // return resultsDictionary;
                throw;
            }
        }

        /// <summary>
        /// Update the index for a named collection's HNSW index. This should be run
        /// infrequently but routinely when the number of records in the collection changes significantly.
        /// </summary>
        /// <param name="collectionName">Name of the collection to update</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary with operation results</returns>
        [McpServerTool, Description("Update the index for a named collection's HNSW index.")]
        public async Task<Dictionary<string, object>> UpdateIndex(
            string collectionName,
            CancellationToken cancellationToken = default)
        {
            var resultsDictionary = new Dictionary<string, object>
            {
                { "success", (bool?)null },
                { "message", (string?)null }
            };
            try
            {
                var updateIndexSql = $@"
                        REINDEX INDEX hnsw_index_{collectionName};
                    ";
                await using (var conn = _connectionFactory.CreateConnection())
                {
                    await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                    await using (var cmd = new NpgsqlCommand(updateIndexSql, conn))
                    {
                        // Add parameters to avoid SQL injection and ensure type safety.
                        cmd.Parameters.AddWithValue("collectionName", collectionName);

                        // ExecuteNonQueryAsync is used because this command does not return rows.
                        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                resultsDictionary["success"] = true;
                resultsDictionary["message"] = $"Successfully updated index for collection {collectionName}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating the index for the collection.");
                throw;
            }
            return resultsDictionary;
        }

        /// <summary>
        /// Delete a collection from the vector database.
        /// </summary>
        /// <param name="collectionName">Name of the collection to delete</param>
        /// <param name="deleteBlobs">Whether to delete associated blobs</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary with operation results</returns>
        public async Task<Dictionary<string, object>> DeleteCollection(
            string collectionName,
            bool deleteBlobs = false,
            CancellationToken cancellationToken = default)
        {
            var resultsDictionary = new Dictionary<string, object>
            {
                { "success", (bool?)null },
                { "message", (string?)null }
            };
            Boolean deletedFromManager = false;
            Boolean deletedFromSchema = false;
            Boolean deletedBlobs = false;
            try
            {
                // Get the vector store from the kernel’s DI container.
                var vectorStore = _kernel.GetRequiredService<VectorStore>();

                if (vectorStore == null)
                {
                    throw new InvalidOperationException("DeleteCollection error: vector store is not configured in DI.");
                }

                // Try to delete from the DB
                try
                {
                    deletedFromManager = await _schemaManagementService.DeleteCollectionFromManager(collectionName);
                }
                catch (Exception ex)
                {
                    // Log the exception and continue
                    _logger.LogError(ex, "An error occurred while removing the collection from the manager.");
                }

                // Try to delete from the schema
                try
                {
                    deletedFromSchema = await _schemaManagementService.DeleteCollectionFromSchema(collectionName);
                }
                catch (Exception ex)
                {
                    // Log the exception and continue
                    _logger.LogError(ex, "An error occurred while removing the collection from the manager.");
                }

                if (deleteBlobs == true)
                {
                    try
                    {
                        Dictionary<string, object> deletedBlobsStatus = await _storageManagementService.DeleteCollectionBlobs(collectionName);
                        deletedBlobs = true;
                    }
                    catch (Exception ex)
                    {
                        // Log the exception and continue
                        _logger.LogError(ex, "An error occurred while removing the blobs from storage.");
                    }
                }

                resultsDictionary["success"] = deletedFromManager && deletedFromSchema && deletedBlobs;
                resultsDictionary["message"] = $"deletedFromManager: {deletedFromManager}, deletedFromSchema: {deletedFromSchema}, deletedFromBlobs: {deletedBlobs}";
                return resultsDictionary;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while ingesting a new file into the collection.");
                // resultsDictionary["success"] = deletedFromManager && deletedFromSchema && deletedBlobs;
                // resultsDictionary["message"] = $"deletedFromManager: {deletedFromManager}, deletedFromSchema: {deletedFromSchema}, deletedFromBlobs: {deletedBlobs}";
                // return resultsDictionary;
                throw;
            }
        }

        /// <summary>
        /// Ingest an uploaded file into a collection.
        /// </summary>
        /// <param name="collectionName">Name of the collection to ingest into</param>
        /// <param name="chunkSize">Size of chunks in words</param>
        /// <param name="chunkOverlapFraction">Fraction of chunk size to overlap</param>
        /// <param name="file">File to ingest</param>
        /// <param name="saveToBlobStorage">Whether to save the file to blob storage</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary with operation results</returns>
        [McpServerTool, Description("Ingest an uploaded file into a collection.")]
        public async Task<Dictionary<string, object>> IngestUploadedFileAsync(
            string collectionName,
            int chunkSize,
            float chunkOverlapFraction,
            IFormFile file,
            bool saveToBlobStorage = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Before getting the embedding service and running the ingest sequence,
                // we are going to make sure that the filename doesn't already exist in the collection.
                bool fileExists = await _schemaManagementService.CheckFileExistsInCollection(file.FileName, collectionName);
                if (fileExists)
                {
                    throw new InvalidOperationException($"File with name {file.FileName} already exists in collection {collectionName}.");
                }

                // Get the appropriate embedding service for the collection
                var embeddingService = await _embeddingServiceFactory.GetEmbeddingServiceForCollectionAsync(collectionName);
                
                // Delegate to the embedding service
                return await embeddingService.IngestUploadedFileAsync(
                    collectionName,
                    chunkSize,
                    chunkOverlapFraction,
                    file,
                    saveToBlobStorage,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ingesting uploaded file into collection {CollectionName}", collectionName);
                throw;
            }
        }

        /// TODO: the returned file path from text and image based file ingestion
        /// is returning a tmp path (e.g. "/var/folders/g1/thgj33w569vcl__zd1zngyrh0000gp/T/c809cdc2-a935-440e-a6b5-32b93097149f.jpg")
        /// we want to replace this with the blob or S3 path for easier API access down the line.
        /// <summary>
        /// Ingest a local file into a collection.
        /// </summary>
        /// <param name="collectionName">Name of the collection to ingest into</param>
        /// <param name="chunkSize">Size of chunks in words</param>
        /// <param name="chunkOverlapFraction">Fraction of chunk size to overlap</param>
        /// <param name="filePath">Path to the file to ingest</param>
        /// <param name="saveToBlobStorage">Whether to save a copy to blob storage</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary with operation results</returns>
        [McpServerTool, Description("Ingest a local file into a collection.")]
        public async Task<Dictionary<string, object>> IngestLocalFileAsync(
            string collectionName,
            int chunkSize,
            float chunkOverlapFraction,
            string filePath,
            bool saveToBlobStorage = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Before getting the embedding service and running the ingest sequence,
                // we are going to make sure that the filename doesn't already exist in the collection.
                string fileName = Path.GetFileName(filePath);
                bool fileExists = await _schemaManagementService.CheckFileExistsInCollection(fileName, collectionName);
                if (fileExists)
                {
                    throw new InvalidOperationException($"File with name {fileName} already exists in collection {collectionName}.");
                }

                // Get the appropriate embedding service for the collection
                var embeddingService = await _embeddingServiceFactory.GetEmbeddingServiceForCollectionAsync(collectionName);
                
                // Delegate to the embedding service
                return await embeddingService.IngestLocalFileAsync(
                    collectionName,
                    chunkSize,
                    chunkOverlapFraction,
                    filePath,
                    saveToBlobStorage,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ingesting local file into collection {CollectionName}", collectionName);
                throw;
            }
        }

        /// <summary>
        /// Ingest text content into a collection.
        /// </summary>
        /// <param name="collectionName">Name of the collection to ingest into</param>
        /// <param name="chunkSize">Size of chunks in words</param>
        /// <param name="chunkOverlapFraction">Fraction of chunk size to overlap</param>
        /// <param name="content">Content to ingest</param>
        /// <param name="saveToBlobStorage">Whether to save a copy to blob storage</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary with operation results</returns>
        [McpServerTool, Description("Ingest text content into a collection.")]
        public async Task<Dictionary<string, object>> NewTextMemoryAsync(
            string collectionName,
            int chunkSize,
            float chunkOverlapFraction,
            string content,
            bool saveToBlobStorage = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Get the appropriate embedding service for the collection
                var embeddingService = await _embeddingServiceFactory.GetEmbeddingServiceForCollectionAsync(collectionName);
                
                // Delegate to the embedding service
                return await embeddingService.NewTextMemoryAsync(
                    collectionName,
                    chunkSize,
                    chunkOverlapFraction,
                    content,
                    saveToBlobStorage,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ingesting text content into collection {CollectionName}", collectionName);
                throw;
            }
        }

        /// <summary>
        /// Remove a file from a collection.
        /// </summary>
        /// <param name="fileName">Name of the file to remove</param>
        /// <param name="collectionName">Name of the collection</param>
        /// <param name="isBlobFile">Flag indicating if the file is in Azure Blob Storage</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary with operation results</returns>
        [McpServerTool, Description("Remove a file from a collection.")]
        public async Task<Dictionary<string, object>> RemoveFileFromCollectionAsync(
            string fileName,
            string collectionName,
            bool isBlobFile = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Get the appropriate embedding service for the collection
                var embeddingService = await _embeddingServiceFactory.GetEmbeddingServiceForCollectionAsync(collectionName);
                
                // Delegate to the embedding service
                return await embeddingService.RemoveFileFromCollectionAsync(
                    fileName,
                    collectionName,
                    isBlobFile,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing file {FileName} from collection {CollectionName}", fileName, collectionName);
                throw;
            }
        }

        /// <summary>
        /// Perform a vector search on a collection.
        /// </summary>
        /// <param name="query">Query string</param>
        /// <param name="collectionName">Name of the collection to search</param>
        /// <param name="nResults">Number of results to return</param>
        /// <param name="timeWindowEnabled">Whether to enable time-window filtering</param>
        /// <param name="windowDays">Number of days to include in the time window</param>
        /// <param name="temporalDecayEnabled">Whether to enable temporal decay</param>
        /// <param name="decayImpact">Impact of temporal decay</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of search results</returns>
        public async Task<List<EmbeddingSearchResult>> TextVectorSearchFixedCollectionAsync(
            string query,
            string collectionName,
            int nResults = 5,
            bool timeWindowEnabled = false,
            int windowDays = 30,
            bool temporalDecayEnabled = false,
            double decayImpact = 0.01,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Get the appropriate embedding service for the collection
                var embeddingService = await _embeddingServiceFactory.GetEmbeddingServiceForCollectionAsync(collectionName);
                
                // Delegate to the embedding service
                return await embeddingService.TextVectorSearchFixedCollectionAsync(
                    query,
                    collectionName,
                    nResults,
                    timeWindowEnabled,
                    windowDays,
                    temporalDecayEnabled,
                    decayImpact,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing text search on collection {CollectionName}", collectionName);
                throw;
            }
        }

        /// <summary>
        /// Perform a semantic search on a collection.
        /// </summary>
        /// <param name="query">Query string</param>
        /// <param name="collectionName">Name of the collection to search</param>
        /// <param name="nResults">Number of results to return</param>
        /// <param name="timeWindowEnabled">Whether to enable time-window filtering</param>
        /// <param name="windowDays">Number of days to include in the time window</param>
        /// <param name="temporalDecayEnabled">Whether to enable temporal decay</param>
        /// <param name="decayImpact">Impact of temporal decay</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of search results</returns>
        public async Task<List<EmbeddingSearchResult>> TextSemanticSearchFixedCollectionAsync(
            string query,
            string collectionName,
            int nResults = 5,
            bool timeWindowEnabled = false,
            int windowDays = 30,
            bool temporalDecayEnabled = false,
            double decayImpact = 0.01,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Get the appropriate embedding service for the collection
                var embeddingService = await _embeddingServiceFactory.GetEmbeddingServiceForCollectionAsync(collectionName);
                
                // Delegate to the embedding service
                return await embeddingService.TextSemanticSearchFixedCollectionAsync(
                    query,
                    collectionName,
                    nResults,
                    timeWindowEnabled,
                    windowDays,
                    temporalDecayEnabled,
                    decayImpact,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing text search on collection {CollectionName}", collectionName);
                throw;
            }
        }

        /// <summary>
        /// Perform a hybrid search on a collection.
        /// </summary>
        /// <param name="query">Query string</param>
        /// <param name="collectionName">Name of the collection to search</param>
        /// <param name="nResults">Number of results to return</param>
        /// <param name="alpha">Weight between vector and semantic search (0.0 to 1.0)</param>
        /// <param name="timeWindowEnabled">Whether to enable time-window filtering</param>
        /// <param name="windowDays">Number of days to include in the time window</param>
        /// <param name="temporalDecayEnabled">Whether to enable temporal decay</param>
        /// <param name="decayImpact">Impact of temporal decay</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of search results</returns>
        [McpServerTool, Description("Perform a text search on a collection.")]
        public async Task<List<EmbeddingSearchResult>> TextSearchFixedCollectionAsync(
            string query,
            string collectionName,
            int nResults = 5,
            double alpha = 0.75,
            bool timeWindowEnabled = false,
            int windowDays = 30,
            bool temporalDecayEnabled = false,
            double decayImpact = 0.01,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Get the appropriate embedding service for the collection
                var embeddingService = await _embeddingServiceFactory.GetEmbeddingServiceForCollectionAsync(collectionName);
                
                // Delegate to the embedding service
                return await embeddingService.TextSearchFixedCollectionAsync(
                    query,
                    collectionName,
                    nResults,
                    alpha,
                    timeWindowEnabled,
                    windowDays,
                    temporalDecayEnabled,
                    decayImpact,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing text search on collection {CollectionName}", collectionName);
                throw;
            }
        }

        /// <summary>
        /// Perform an image-based search on a collection.
        /// </summary>
        /// <param name="image">Image file to use as a query</param>
        /// <param name="collectionName">Name of the collection to search</param>
        /// <param name="nResults">Number of results to return</param>
        /// <param name="timeWindowEnabled">Whether to enable time-window filtering</param>
        /// <param name="windowDays">Number of days to include in the time window</param>
        /// <param name="temporalDecayEnabled">Whether to enable temporal decay</param>
        /// <param name="decayImpact">Impact of temporal decay</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of search results</returns>
        [McpServerTool, Description("Perform an image search on a collection.")]
        public async Task<List<EmbeddingSearchResult>> ImageSearchFixedCollectionAsync(
            IFormFile image,
            string collectionName,
            int nResults = 5,
            bool timeWindowEnabled = false,
            int windowDays = 30,
            bool temporalDecayEnabled = false,
            double decayImpact = 0.01,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Get the appropriate embedding service for the collection
                var embeddingService = await _embeddingServiceFactory.GetEmbeddingServiceForCollectionAsync(collectionName);
                
                // Check if the collection is an image collection
                string embeddingServiceId;
                string embeddingServiceModality;
                (embeddingServiceId, embeddingServiceModality) = await _schemaManagementService.getDefaultEmbeddingServiceDetails(collectionName);
                
                if (embeddingServiceModality != "ImageEmbedding")
                {
                    _logger.LogWarning("Image search is not supported for collection {CollectionName} with modality {Modality}", collectionName, embeddingServiceModality);
                    throw new NotImplementedException("image-to-image search is only available for ImageEmbedding collections.");
                }
                
                // Delegate to the embedding service
                return await embeddingService.ImageSearchFixedCollectionAsync(
                    image,
                    collectionName,
                    nResults,
                    timeWindowEnabled,
                    windowDays,
                    temporalDecayEnabled,
                    decayImpact,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing image search on collection {CollectionName}", collectionName);
                throw;
            }
        }
    }
}
