using Microsoft.SemanticKernel;
using Microsoft.Extensions.VectorData;
using Npgsql;
using PgVectorDynamicRAG.Data;
using PgVectorDynamicRAG.Factories;
using Path = System.IO.Path;
using SkiaSharp;

#pragma warning disable SKEXP0001

namespace PgVectorDynamicRAG.Services
{
    /// <summary>
    /// Service for handling image-based embeddings and retrieval.
    /// This service specializes in processing image files, generating image embeddings using CLIP,
    /// and performing image-based searches.
    /// </summary>
    public class ImageEmbeddingService : AbstractEmbeddingService
    {
        private readonly ILogger<ImageEmbeddingService> _typedLogger;
        private readonly string _imageEmbeddingServiceId;
        private readonly string _clipModelDeploymentName;
        private readonly string _clipModelEndpoint;
        private readonly string _clipModelEndpointApiKey;
        private readonly IHttpClientFactory _httpClientFactory;

        /// <summary>
        /// Constructor for the image embedding service.
        /// </summary>
        /// <param name="kernel">The Semantic Kernel instance</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="connectionFactory">Postgres connection factory</param>
        /// <param name="schemaManagementService">Schema management service</param>
        /// <param name="blobManagementService">Blob management service</param>
        public ImageEmbeddingService(
            Kernel kernel,
            ILogger<ImageEmbeddingService> logger,
            IPostgresConnectionFactory connectionFactory,
            SchemaManagementService schemaManagementService,
            AbstractStorageService storageManagementService,
            IHttpClientFactory httpClientFactory)
            : base(kernel, (ILogger<object>)(object)logger, connectionFactory, schemaManagementService, storageManagementService)
        {
            _typedLogger = logger;

            // Get CLIP model configuration from environment variables
            _clipModelDeploymentName = Environment.GetEnvironmentVariable("CLIP_MODEL_DEPLOYMENT_NAME");
            _clipModelEndpoint = Environment.GetEnvironmentVariable("CLIP_MODEL_ENDPOINT");
            _clipModelEndpointApiKey = Environment.GetEnvironmentVariable("CLIP_MODEL_ENDPOINT_API_KEY");

            _httpClientFactory = httpClientFactory;

            if (string.IsNullOrEmpty(_clipModelEndpoint) || string.IsNullOrEmpty(_clipModelEndpointApiKey))
            {
                _typedLogger.LogWarning("CLIP model endpoint or API key is not configured. Image embedding functionality may be limited.");
            }
            else
            {
                _typedLogger.LogInformation("CLIP model endpoint configured: {DeploymentName}", _clipModelDeploymentName);
            }
        }

        /// <summary>
        /// Processes an image file, generates its embedding using the CLIP model,
        /// and stores the embedding in the specified collection.
        /// This method handles the entire flow from file loading, preprocessing,
        /// embedding generation, to storing the record in the vector store.
        /// It also manages temporary files and handles errors gracefully.
        /// </summary>
        /// <param name="collectionName"></param>
        /// <param name="filePath"></param>
        /// <param name="fileName"></param>
        /// <param name="deleteTempFile"></param>
        /// <param name="saveToBlobStorage"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        private async Task<Dictionary<string, object>> ProcessFileAsync(
            string collectionName,
            string filePath,
            string fileName,
            bool deleteTempFile,
            bool saveToBlobStorage,
            CancellationToken cancellationToken)
        {
            var resultsDictionary = new Dictionary<string, object>
            {
                { "success", (bool?)null },
                { "message", (string?)null },
                { "file_name", fileName },
                { "file_path", filePath }
            };

            string preprocessedImagePath = null;

            try
            {
                // Collection identification
                _logger.LogInformation("Collection identification starting [1/4]");
                bool collectionExists = await _schemaManagementService.CheckCollectionExists(collectionName);
                string embeddingServiceId;
                string embeddingServiceModality;

                if (!collectionExists)
                {
                    throw new InvalidOperationException($"Collection named {collectionName} does not exist.");
                }
                else
                {
                    (embeddingServiceId, embeddingServiceModality) = await _schemaManagementService.getDefaultEmbeddingServiceDetails(collectionName);
                }
                _logger.LogInformation("Collection identification complete [step 1 of 4]");

                // Loader: Determine file type from the file extension.
                string extension = Path.GetExtension(fileName).ToLowerInvariant();
                FileType fileType = extension switch
                {
                    ".png" => FileType.Png,
                    ".jpeg" => FileType.Jpeg,
                    ".jpg" => FileType.Jpeg,
                    ".bmp" => FileType.Bmp,
                    _ => throw new NotSupportedException($"File extension {extension} is not supported.")
                };

                // Loader: Get the appropriate loader from the factory.
                IFileLoader fileLoader = FileLoaderFactory.GetLoader(fileType);
                object rawData = fileLoader.LoadFile(filePath);
                _logger.LogInformation("Data load complete [step 2 of 4]");

                // Preprocess the image for the CLIP model (resize to 336x335)
                preprocessedImagePath = Path.Combine(Path.GetTempPath(), $"preprocessed_{Path.GetFileName(filePath)}");
                await PreprocessImageForClipAsync(filePath, preprocessedImagePath);
                _logger.LogInformation("Image preprocessing complete for CLIP model");

                // Get the vector store and collection
                var vectorStore = _kernel.GetRequiredService<VectorStore>();
                var collection = vectorStore.GetCollection<Guid, EmbeddingChunkRecord>(collectionName);

                // Generate embedding using CLIP model
                _logger.LogInformation("Generating image embedding using CLIP model");
                float[] embedding = await GenerateClipImageEmbeddingAsync(preprocessedImagePath, cancellationToken);

                // Log embedding details for debugging
                _logger.LogInformation("Generated embedding with {Dimensions} dimensions", embedding.Length);
                _logger.LogDebug("First few embedding values: {Values}",
                    string.Join(", ", embedding.Take(5).Select(v => v.ToString("F4"))));
                _logger.LogDebug("Embedding L2 norm: {Norm}",
                    Math.Sqrt(embedding.Sum(x => x * x)).ToString("F4"));

                // If requested, save a copy to blob storage
                if (saveToBlobStorage)
                {
                    string blobPath = $"{collectionName}/{fileName}";
                    await SaveFileToBlobStorageAsync(filePath, blobPath);
                    // resultsDictionary["blob_path"] = blobPath;
                }

                // Create a record for the image
                var record = new EmbeddingChunkRecord
                {
                    Key = Guid.NewGuid(),
                    FileName = fileName,
                    PageId = 0,
                    ChunkIndex = 0,
                    Definition = $"Image: {fileName}", // Simple description, could be enhanced with image analysis
                    EmbeddingServiceId = embeddingServiceId,
                    DefinitionEmbedding = embedding,
                    Timestamp = DateTime.UtcNow,
                    Modality = "ImageEmbedding"
                };

                // Upsert the record into the collection
                _logger.LogInformation("Upserting image record into {CollectionName}...", collectionName);
                await collection.UpsertAsync(record, cancellationToken);

                // Clean up temporary files
                if (preprocessedImagePath != null && File.Exists(preprocessedImagePath))
                {
                    try
                    {
                        File.Delete(preprocessedImagePath);
                        _logger.LogInformation("Deleted preprocessed image file: {TempFilePath}", preprocessedImagePath);
                    }
                    catch (Exception ex)
                    {
                        _typedLogger.LogWarning(ex, "Failed to delete preprocessed image file: {TempFilePath}", preprocessedImagePath);
                    }
                }

                if (deleteTempFile && File.Exists(filePath))
                {
                    try
                    {
                        File.Delete(filePath);
                        _logger.LogInformation("Deleted temporary file: {TempFilePath}", filePath);
                    }
                    catch (Exception ex)
                    {
                        _typedLogger.LogWarning(ex, "Failed to delete temporary file: {TempFilePath}", filePath);
                    }
                }

                _logger.LogInformation("Successfully ingested image file '{FileName}' into collection '{CollectionName}'.",
                    fileName, collectionName);

                resultsDictionary["success"] = true;
                resultsDictionary["message"] = $"Successfully ingested image file {fileName} into collection {collectionName}.";
                resultsDictionary["chunk_count"] = 1;

                _logger.LogInformation("Image Embedder complete [4/4 complete]");
                return resultsDictionary;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing file: {ErrorMessage}", ex.Message);
                resultsDictionary["success"] = false;
                resultsDictionary["message"] = $"ProcessFileAsync error: {ex.Message}";

                // Clean up any temporary files in case of error
                if (preprocessedImagePath != null && File.Exists(preprocessedImagePath))
                {
                    try
                    {
                        File.Delete(preprocessedImagePath);
                    }
                    catch
                    {
                        // Ignore cleanup errors in error handler
                    }
                }

                return resultsDictionary;
            }
        }

        /// <summary>
        /// Ingest an uploaded image file into an image collection.
        /// </summary>
        /// <param name="collectionName">Name of the collection to ingest into</param>
        /// <param name="chunkSize">Not used for images, but kept for API consistency</param>
        /// <param name="chunkOverlapFraction">Not used for images, but kept for API consistency</param>
        /// <param name="file">Image file to ingest</param>
        /// <param name="saveToBlobStorage">Whether to save the file to blob storage</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary with operation results</returns>
        public override async Task<Dictionary<string, object>> IngestUploadedFileAsync(
            string collectionName,
            int chunkSize,
            float chunkOverlapFraction,
            IFormFile file,
            bool saveToBlobStorage = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                bool collectionExists = await _schemaManagementService.CheckCollectionExists(collectionName, cancellationToken);
                if (!collectionExists)
                {
                    throw new InvalidOperationException($"Collection with name {collectionName} does not exist.");
                }

                // Validate that the collection exists and has the correct modality
                if (!await ValidateCollectionModalityAsync(collectionName, "ImageEmbedding"))
                {
                    throw new InvalidOperationException($"Collection {collectionName} is not of ImageEmbedding modality.");
                }

                // Check if the file is an image
                string contentType = file.ContentType.ToLowerInvariant();
                if (!contentType.StartsWith("image/"))
                {
                    throw new InvalidOperationException($"Collection {collectionName} is not of ImageEmbedding modality.");
                }

                // Save the file to a temporary location
                string tempFilePath = Path.GetTempFileName();

                try
                {
                    using (var stream = new FileStream(tempFilePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream, cancellationToken);
                    }

                    // Process the image file using the new ProcessFileAsync method
                    return await ProcessFileAsync(
                        collectionName,
                        tempFilePath,
                        file.FileName,
                        true, // Delete temp file when done
                        saveToBlobStorage,
                        cancellationToken);
                }
                finally
                {
                    // Ensure temp file is deleted if an exception occurs
                    if (File.Exists(tempFilePath))
                    {
                        try
                        {
                            File.Delete(tempFilePath);
                        }
                        catch (Exception ex)
                        {
                            _typedLogger.LogWarning(ex, "Failed to delete temporary file: {TempFilePath}", tempFilePath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _typedLogger.LogError(ex, "Error ingesting uploaded image file into collection {CollectionName}", collectionName);
                throw new InvalidOperationException($"Error ingesting image file: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Ingest a local image file into a collection.
        /// </summary>
        /// <param name="collectionName">Name of the collection to ingest into</param>
        /// <param name="chunkSize">Not used for images, but kept for API consistency</param>
        /// <param name="chunkOverlapFraction">Not used for images, but kept for API consistency</param>
        /// <param name="filePath">Path to the image file to ingest</param>
        /// <param name="saveToBlobStorage">Whether to save the file to blob storage</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary with operation results</returns>
        public override async Task<Dictionary<string, object>> IngestLocalFileAsync(
            string collectionName,
            int chunkSize,
            float chunkOverlapFraction,
            string filePath,
            bool saveToBlobStorage = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate that the collection exists and has the correct modality
                if (!await ValidateCollectionModalityAsync(collectionName, "ImageEmbedding"))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", $"Collection {collectionName} does not exist or does not have ImageEmbedding modality." }
                    };
                }

                // Check if the file exists
                if (!File.Exists(filePath))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", $"File not found: {filePath}" }
                    };
                }

                // Check if the file is an image
                string extension = Path.GetExtension(filePath).ToLowerInvariant();
                if (extension != ".jpg" && extension != ".jpeg" && extension != ".png" && extension != ".bmp")
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", $"File is not a supported image format. Supported formats: jpg, jpeg, png, bmp" }
                    };
                }

                // Process the image file using the new ProcessFileAsync method
                string fileName = Path.GetFileName(filePath);
                return await ProcessFileAsync(
                    collectionName,
                    filePath,
                    fileName,
                    false, // Don't delete the original file
                    saveToBlobStorage,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _typedLogger.LogError(ex, "Error ingesting local image file into collection {CollectionName}", collectionName);
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error ingesting image file: {ex.Message}" }
                };
            }
        }

        /// <summary>
        /// This method is not implemented for image embeddings.
        /// The abstract class requires it, but it's not applicable for image collections.
        /// </summary>
        /// <param name="collectionName">Name of the collection</param>
        /// <param name="chunkSize">Not used</param>
        /// <param name="chunkOverlapFraction">Not used</param>
        /// <param name="content">Not used</param>
        /// <param name="saveToBlobStorage">Not used</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary with error message</returns>
        public override async Task<Dictionary<string, object>> NewTextMemoryAsync(
            string collectionName,
            int chunkSize,
            float chunkOverlapFraction,
            string content,
            bool saveToBlobStorage = false,
            CancellationToken cancellationToken = default)
        {
            // Log that image search is not supported for text collections
            _typedLogger.LogWarning("Creating a new text memory is not supported in image collection: {CollectionName}", collectionName);

            // Throw NotImplementedException to indicate this functionality is intentionally not supported
            // This will be caught by the controller and returned as a 501 Not Implemented response
            throw new NotImplementedException("Creating a new text memory is not supported in image collection.");
        }

        // /// <summary>
        // /// Perform a vector search on a collection using text to find images.
        // /// </summary>
        // /// <param name="query">Text query to find images</param>
        // /// <param name="collectionName">Name of the collection to search</param>
        // /// <param name="nResults">Number of results to return</param>
        // /// <param name="alpha">Alpha parameter trades off BM25 and vector search during RRF</param>
        // /// <param name="timeWindowEnabled">Whether to enable time-window filtering</param>
        // /// <param name="windowDays">Number of days to include in the time window</param>
        // /// <param name="temporalDecayEnabled">Whether to apply exponential decay based on timestamp</param>
        // /// <param name="decayImpact">Decay impact factor for exponential decay. 0.005 - low, 0.01 - medium, 0.05 - high</param>
        // /// <param name="cancellationToken">Cancellation token</param>
        // /// <returns>List of search results</returns>
        // public override async Task<List<EmbeddingSearchResult>> TextSearchFixedCollectionAsync(
        //     string query,
        //     string collectionName,
        //     int? nResults = 5,
        //     double alpha = 0.75,
        //     bool timeWindowEnabled = false,
        //     int windowDays = 30,
        //     bool temporalDecayEnabled = false,
        //     double decayImpact = 0.01,
        //     CancellationToken cancellationToken = default)
        // {
        //     List<EmbeddingSearchResult> results;

        //     try
        //     {
        //         // Check to make sure that the collection exists
        //         bool collectionExists = await _schemaManagementService.CheckCollectionExists(collectionName, cancellationToken: CancellationToken.None);
        //         if (!collectionExists)
        //         {
        //             throw new InvalidOperationException($"Collection named {collectionName} does not exist.");
        //         }
        //         else
        //         {
        //             string embeddingServiceId = await _schemaManagementService.getDefaultEmbeddingServiceId(collectionName);
        //             string validatedEmbeddingModelName = await _schemaManagementService.getEmbeddingServiceName(embeddingServiceId);
        //             _typedLogger.LogInformation($"Embedding model {validatedEmbeddingModelName} is valid for collection {collectionName}");

        //             // Calculate the time window cutoff date if time-window filtering is enabled
        //             DateTime? cutoffDate = null;
        //             if (timeWindowEnabled)
        //             {
        //                 cutoffDate = DateTime.UtcNow.AddDays(-windowDays);
        //                 _typedLogger.LogInformation($"Time-window filtering enabled. Only including results after {cutoffDate}.");
        //             }

        //             results = await VectorSearchAsync(
        //                 collectionName,
        //                 validatedEmbeddingModelName,
        //                 embeddingServiceId,
        //                 query,
        //                 nResults,
        //                 cutoffDate,
        //                 cancellationToken
        //             );
        //         }

        //         return results;
        //     }
        //     catch (Exception ex)
        //     {
        //         _typedLogger.LogError(ex, "An error occurred while searching the fixed collection.");
        //         return new List<EmbeddingSearchResult>();
        //     }
        // }

        /// <summary>
        /// This method is not implemented for image embeddings.
        /// The abstract class requires it, but it's not applicable for image collections.
        /// </summary>
        /// <param name="query">Text query to search for</param>
        /// <param name="collectionName">Name of the collection to search</param>
        /// <param name="nResults">Number of results to return</param>
        /// <param name="timeWindowEnabled">Whether to enable time-window filtering</param>
        /// <param name="windowDays">Number of days to include in the time window</param>
        /// <param name="temporalDecayEnabled">Whether to apply temporal decay based on timestamp</param>
        /// <param name="decayImpact">Decay impact factor for exponential decay. 0.005 - low, 0.01 - medium, 0.05 - high</param>
        /// /// <param name="cancellationToken">Cancellation token</param>
        public override async Task<List<EmbeddingSearchResult>> TextSemanticSearchFixedCollectionAsync(
            string query,
            string collectionName,
            int? nResults = 5,
            bool timeWindowEnabled = false,
            int windowDays = 30,
            bool temporalDecayEnabled = false,
            double decayImpact = 0.01,
            CancellationToken cancellationToken = default)
        {
            // Log that image search is not supported for text collections
            _typedLogger.LogWarning("Text semantic search is not supported for image collections ({CollectionName}), use ImageSearchFixedCollectionAsync", collectionName);

            // Throw NotImplementedException to indicate this functionality is intentionally not supported
            // This will be caught by the controller and returned as a 501 Not Implemented response
            throw new NotImplementedException("Creating a new text memory is not supported in image collection.");
        }

        /// <summary>
        /// This method is not implemented for image embeddings.
        /// The abstract class requires it, but it's not applicable for image collections.
        /// </summary>

        // public override async Task<List<EmbeddingSearchResult>> TextVectorSearchFixedCollectionAsync(
        //     string query,
        //     string collectionName,
        //     int? nResults = 5,
        //     bool timeWindowEnabled = false,
        //     int windowDays = 30,
        //     bool temporalDecayEnabled = false,
        //     double decayImpact = 0.01,
        //     CancellationToken cancellationToken = default)
        // {
        //     // Log that image search is not supported for text collections
        //     _typedLogger.LogWarning("Text vector search is not supported for image collections ({CollectionName}), use ImageSearchFixedCollectionAsync", collectionName);

        //     // Throw NotImplementedException to indicate this functionality is intentionally not supported
        //     // This will be caught by the controller and returned as a 501 Not Implemented response
        //     throw new NotImplementedException("Creating a new text memory is not supported in image collection.");
        // }

        public override async Task<List<EmbeddingSearchResult>> TextVectorSearchFixedCollectionAsync(
            string query,
            string collectionName,
            int? nResults = 5,
            bool timeWindowEnabled = false,
            int windowDays = 30,
            bool temporalDecayEnabled = false,
            double decayImpact = 0.01,
            CancellationToken cancellationToken = default)
        {
            // Route the request to TextSearchFixedCollectionAsync, using default alpha=0.75
            return await TextSearchFixedCollectionAsync(
                query,
                collectionName,
                nResults,
                alpha: 0.75, // or another default if you prefer
                timeWindowEnabled,
                windowDays,
                temporalDecayEnabled,
                decayImpact,
                cancellationToken);
        }

        // /// <summary>
        // /// Searches a vector collection for the most similar embeddings to the given query.
        // /// </summary>
        // /// <param name="collectionName">Name of the collection to search</param>
        // /// <param name="embeddingModelName">Name of the embedding model</param>
        // /// <param name="embeddingServiceId">ID of the embedding service</param>
        // /// <param name="query">Text query to find images</param>
        // /// <param name="nResults">Number of results to return</param>
        // /// <param name="cutoffDate">Optional cutoff date for filtering results</param>
        // /// <param name="cancellationToken">Cancellation token</param>
        // /// <returns>List of search results</returns>
        // private async Task<List<EmbeddingSearchResult>> VectorSearchAsync(
        //     string collectionName,
        //     string embeddingModelName,
        //     string embeddingServiceId,
        //     string query,
        //     int? nResults = null,
        //     DateTime? cutoffDate = null,
        //     CancellationToken cancellationToken = default)
        // {
        //     var textEmbeddingService = _kernel.GetRequiredService<ITextEmbeddingGenerationService>(embeddingServiceId);

        //     // Generate embedding for the query
        //     var searchVector = await textEmbeddingService.GenerateEmbeddingAsync(query);

        //     var top = nResults ?? 10;

        //     var sql = @$"
        //         SELECT *
        //         FROM ""{collectionName}""
        //         WHERE ""EmbeddingServiceId"" = @embeddingServiceId
        //         {(cutoffDate != null ? "AND \"Timestamp\" >= @cutoffDate" : "")}
        //         ORDER BY ""DefinitionEmbedding"" <-> @embedding
        //         LIMIT {top}";

        //     using (var connection = _connectionFactory.CreateConnection())
        //     {
        //         await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        //         using (var command = new NpgsqlCommand(sql, connection))
        //         {
        //             var embedding = new Pgvector.Vector(searchVector);
        //             command.Parameters.AddWithValue("@embedding", embedding);
        //             command.Parameters["@embedding"].DataTypeName = "vector";
        //             command.Parameters.AddWithValue("@embeddingServiceId", embeddingServiceId);

        //             if (cutoffDate != null)
        //             {
        //                 command.Parameters.AddWithValue("@cutoffDate", cutoffDate);
        //             }

        //             await using (var reader = await command.ExecuteReaderAsync())
        //             {
        //                 // Process the results
        //                 var embeddingsList = new List<EmbeddingSearchResult>();
        //                 var i = 1;
        //                 while (await reader.ReadAsync())
        //                 {
        //                     var record = new
        //                     {
        //                         FileName = reader.GetString(reader.GetOrdinal("FileName")),
        //                         ChunkIndex = reader.GetInt32(reader.GetOrdinal("ChunkIndex")),
        //                         Definition = reader.GetString(reader.GetOrdinal("Definition")),
        //                         DefinitionEmbedding = reader.GetFieldValue<Pgvector.Vector>(reader.GetOrdinal("DefinitionEmbedding")),
        //                         Timestamp = reader.IsDBNull(reader.GetOrdinal("Timestamp")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("Timestamp"))
        //                     };

        //                     double cosineDistance = CalculateCosineDistance(record.DefinitionEmbedding.ToArray(), searchVector.ToArray());

        //                     EmbeddingSearchResult result = new EmbeddingSearchResult(
        //                         i,
        //                         cosineDistance,
        //                         record.FileName,
        //                         record.ChunkIndex,
        //                         record.Definition,
        //                         embeddingModelName,
        //                         record.Timestamp
        //                     );

        //                     embeddingsList.Add(result);
        //                     i++;
        //                 }

        //                 return embeddingsList;
        //             }
        //         }
        //     }
        // }

        /// <summary>
        /// Perform an image-based search on a collection using an uploaded image as a query.
        /// </summary>
        /// <param name="query">Image file to use as a query</param>
        /// <param name="collectionName">Name of the collection to search</param>
        /// <param name="nResults">Number of results to return</param>
        /// <param name="timeWindowEnabled">Whether to enable time-window filtering</param>
        /// <param name="windowDays">Number of days to include in the time window</param>
        /// <param name="temporalDecayEnabled">Whether to enable temporal decay</param>
        /// <param name="decayImpact">Impact of temporal decay</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of search results</returns>
        public override async Task<List<EmbeddingSearchResult>> ImageSearchFixedCollectionAsync(
            IFormFile query,
            string collectionName,
            int? nResults = 5,
            bool? timeWindowEnabled = false,
            int? windowDays = 30,
            bool? temporalDecayEnabled = false,
            double? decayImpact = 0.01,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _typedLogger.LogInformation("Starting image search on collection {CollectionName}", collectionName);

                // Check if collection exists
                bool collectionExists = await _schemaManagementService.CheckCollectionExists(collectionName);
                if (!collectionExists)
                {
                    throw new InvalidOperationException($"Collection named {collectionName} does not exist.");
                }

                // Get the embedding service ID for this collection
                string embeddingServiceId;
                string embeddingServiceModality;
                (embeddingServiceId, embeddingServiceModality) = await _schemaManagementService.getDefaultEmbeddingServiceDetails(collectionName);
                string embeddingModelName = await _schemaManagementService.getEmbeddingServiceName(embeddingServiceId);

                // Save the uploaded image to a temporary file
                string tempFilePath = Path.GetTempFileName();
                string fileName = query.FileName;

                using (var stream = new FileStream(tempFilePath, FileMode.Create))
                {
                    await query.CopyToAsync(stream, cancellationToken);
                }

                // Preprocess the image for the CLIP model
                string preprocessedImagePath = Path.Combine(Path.GetTempPath(), $"preprocessed_{Path.GetFileName(tempFilePath)}");
                await PreprocessImageForClipAsync(tempFilePath, preprocessedImagePath);

                // Generate embedding for the query image using CLIP model
                _typedLogger.LogInformation("Generating embedding for query image");
                float[] queryEmbedding = await GenerateClipImageEmbeddingAsync(preprocessedImagePath, cancellationToken);

                // Apply time window filter if enabled
                DateTime? cutoffDate = null;
                if (timeWindowEnabled.HasValue && timeWindowEnabled.Value == true && windowDays.HasValue)
                {
                    cutoffDate = DateTime.UtcNow.AddDays(-windowDays.Value);
                    _typedLogger.LogInformation("Time-window filtering enabled. Only including results after {CutoffDate}", cutoffDate);
                }

                // Get the vector store and collection
                var vectorStore = _kernel.GetRequiredService<VectorStore>();
                var collection = vectorStore.GetCollection<Guid, EmbeddingChunkRecord>(collectionName);

                // Prepare SQL query for vector search
                var top = nResults ?? 5;

                var sql = @$"
                    SELECT *
                    FROM ""{collectionName}""
                    WHERE ""EmbeddingServiceId"" = @embeddingServiceId
                    {(cutoffDate != null ? "AND \"Timestamp\" >= @cutoffDate" : "")}
                    ORDER BY ""DefinitionEmbedding"" <-> @embedding
                    LIMIT {top}";

                using (var connection = _connectionFactory.CreateConnection())
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                    using (var command = new NpgsqlCommand(sql, connection))
                    {
                        var embedding = new Pgvector.Vector(queryEmbedding);
                        command.Parameters.AddWithValue("@embedding", embedding);
                        command.Parameters["@embedding"].DataTypeName = "vector";
                        command.Parameters.AddWithValue("@embeddingServiceId", embeddingServiceId);

                        if (cutoffDate != null)
                        {
                            command.Parameters.AddWithValue("@cutoffDate", cutoffDate);
                        }

                        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            // Process the results
                            var embeddingsList = new List<EmbeddingSearchResult>();
                            var i = 1;

                            while (await reader.ReadAsync(cancellationToken))
                            {
                                var record = new
                                {
                                    FileName = reader.GetString(reader.GetOrdinal("FileName")),
                                    ChunkIndex = reader.GetInt32(reader.GetOrdinal("ChunkIndex")),
                                    Definition = reader.GetString(reader.GetOrdinal("Definition")),
                                    DefinitionEmbedding = reader.GetFieldValue<Pgvector.Vector>(reader.GetOrdinal("DefinitionEmbedding")),
                                    Timestamp = reader.IsDBNull(reader.GetOrdinal("Timestamp")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("Timestamp"))
                                };

                                // Calculate cosine distance between query embedding and result embedding
                                double cosineDistance = CalculateCosineDistance(record.DefinitionEmbedding.ToArray(), queryEmbedding);

                                // Apply temporal decay if enabled
                                if (temporalDecayEnabled.HasValue && temporalDecayEnabled.Value == true && record.Timestamp != DateTime.MinValue && decayImpact.HasValue)
                                {
                                    // Apply exponential decay based on the timestamp
                                    double decayMultiplier = ApplyExponentialDecay(record.Timestamp, DateTime.UtcNow, decayImpact.Value);
                                    cosineDistance *= decayMultiplier;
                                    _typedLogger.LogDebug("Applied temporal decay to result {Index}. Original score: {OriginalScore}, New score: {NewScore}",
                                        i, cosineDistance / decayMultiplier, cosineDistance);
                                }

                                // Create the search result
                                EmbeddingSearchResult result = new EmbeddingSearchResult(
                                    i,
                                    cosineDistance,
                                    record.FileName,
                                    record.ChunkIndex,
                                    record.Definition,
                                    embeddingModelName,
                                    record.Timestamp
                                );

                                // Get raw image data from blob storage
                                try
                                {
                                    // Construct the blob path based on collection name and filename
                                    // The record.FileName might already contain the full path within the collection
                                    // We need to ensure we're constructing the correct blob path
                                    string blobPath;

                                    // If the filename already starts with the collection name, use it as is
                                    if (record.FileName.StartsWith($"{collectionName}/", StringComparison.OrdinalIgnoreCase))
                                    {
                                        blobPath = record.FileName;
                                    }
                                    // If it contains a path but doesn't start with collection name, prepend the collection name
                                    // If it's just a filename without path, construct the path with collection name
                                    else
                                    {
                                        blobPath = $"{collectionName}/{record.FileName}";
                                    }
                                    _typedLogger.LogInformation("Retrieving image data from blob storage: {BlobPath}", blobPath);

                                    // Download the image to a memory stream
                                    using (var imageStream = new MemoryStream())
                                    {
                                        await _storageManagementService.DownloadBlobToStreamAsync(_storageManagementService.ContainerName, blobPath, imageStream);

                                        // Convert the image data to a Base64 string
                                        byte[] imageBytes = imageStream.ToArray();
                                        string base64Image = Convert.ToBase64String(imageBytes);

                                        // Add the image data to the result
                                        result.ImageData = base64Image;
                                        _typedLogger.LogInformation("Successfully retrieved image data for {FileName}", record.FileName);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    // Log the error but continue processing other results
                                    _typedLogger.LogWarning(ex, "Failed to retrieve image data for {FileName}: {ErrorMessage}", record.FileName, ex.Message);
                                }

                                embeddingsList.Add(result);
                                i++;
                            }

                            // Clean up temporary files
                            try
                            {
                                if (File.Exists(tempFilePath))
                                {
                                    File.Delete(tempFilePath);
                                }
                                if (File.Exists(preprocessedImagePath))
                                {
                                    File.Delete(preprocessedImagePath);
                                }
                            }
                            catch (Exception ex)
                            {
                                _typedLogger.LogWarning(ex, "Failed to delete temporary files");
                            }

                            return embeddingsList;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _typedLogger.LogError(ex, "An error occurred during image search: {ErrorMessage}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Performs a text-to-image search on an image collection using a text query.
        /// </summary>
        /// <param name="query">Text query to search for</param>
        /// <param name="collectionName">Name of the collection to search</param>
        /// <param name="nResults">Number of results to return</param>
        /// <param name="alpha">Alpha parameter trades off BM25 and vector search during RRF</param>
        /// <param name="timeWindowEnabled">Whether to enable time-window filtering</param>
        /// <param name="windowDays">Number of days to include in the time window</param>
        /// <param name="temporalDecayEnabled">Whether to apply temporal decay based on timestamp</param>
        /// <param name="decayImpact">Decay impact factor for exponential decay. 0.005 - low, 0.01 - medium, 0.05 - high</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public override async Task<List<EmbeddingSearchResult>> TextSearchFixedCollectionAsync(
            string query,
            string collectionName,
            int? nResults = 5,
            double alpha = 0.75,
            bool timeWindowEnabled = false,
            int windowDays = 30,
            bool temporalDecayEnabled = false,
            double decayImpact = 0.01,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _typedLogger.LogInformation("Starting text-to-image search on collection {CollectionName}", collectionName);

                // Check if collection exists
                bool collectionExists = await _schemaManagementService.CheckCollectionExists(collectionName);
                if (!collectionExists)
                {
                    throw new InvalidOperationException($"Collection named {collectionName} does not exist.");
                }

                // Get the embedding service ID for this collection
                string embeddingServiceId;
                string embeddingServiceModality;
                (embeddingServiceId, embeddingServiceModality) = await _schemaManagementService.getDefaultEmbeddingServiceDetails(collectionName);
                string embeddingModelName = await _schemaManagementService.getEmbeddingServiceName(embeddingServiceId);

                // Check if the text query is within token limits (max 77 tokens for CLIP)
                const int maxClipTokens = 77;
                int tokenCount = Embeddings.TokenCheckUtility.GetTokenCount(query, "cl100k_base", _typedLogger);
                if (tokenCount > maxClipTokens)
                {
                    _typedLogger.LogWarning("Text query exceeds CLIP model token limit of {MaxTokens}. Current tokens: {TokenCount}. Query will be truncated.", maxClipTokens, tokenCount);
                    // We could truncate here, but for now we'll just warn and let the CLIP model handle it
                }

                // Generate embedding for the text query using CLIP model
                _typedLogger.LogInformation("Generating embedding for text query: '{Query}'", query);
                float[] queryEmbedding = await GenerateClipTextEmbeddingAsync(query, cancellationToken);

                // Apply time window filter if enabled
                DateTime? cutoffDate = null;
                if (timeWindowEnabled)
                {
                    cutoffDate = DateTime.UtcNow.AddDays(-windowDays);
                    _typedLogger.LogInformation("Time-window filtering enabled. Only including results after {CutoffDate}", cutoffDate);
                }

                // Get the vector store and collection
                var vectorStore = _kernel.GetRequiredService<VectorStore>();
                var collection = vectorStore.GetCollection<Guid, EmbeddingChunkRecord>(collectionName);

                var topKMultiplier = 3;
                var top = (nResults ?? 10) * topKMultiplier;

                var sql = @$"
                    SELECT *
                    FROM ""{collectionName}""
                    WHERE ""EmbeddingServiceId"" = @embeddingServiceId
                    {(cutoffDate != null ? "AND \"Timestamp\" >= @cutoffDate" : "")}
                    ORDER BY ""DefinitionEmbedding"" <-> @embedding
                    LIMIT {top}";

                using (var connection = _connectionFactory.CreateConnection())
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                    using (var command = new NpgsqlCommand(sql, connection))
                    {
                        var embedding = new Pgvector.Vector(queryEmbedding);
                        command.Parameters.AddWithValue("@embedding", embedding);
                        command.Parameters["@embedding"].DataTypeName = "vector";
                        command.Parameters.AddWithValue("@embeddingServiceId", embeddingServiceId);

                        if (cutoffDate != null)
                        {
                            command.Parameters.AddWithValue("@cutoffDate", cutoffDate);
                        }

                        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            // Process the results
                            var embeddingsList = new List<EmbeddingSearchResult>();
                            var i = 1;

                            while (await reader.ReadAsync(cancellationToken))
                            {
                                var record = new
                                {
                                    FileName = reader.GetString(reader.GetOrdinal("FileName")),
                                    ChunkIndex = reader.GetInt32(reader.GetOrdinal("ChunkIndex")),
                                    Definition = reader.GetString(reader.GetOrdinal("Definition")),
                                    DefinitionEmbedding = reader.GetFieldValue<Pgvector.Vector>(reader.GetOrdinal("DefinitionEmbedding")),
                                    Timestamp = reader.IsDBNull(reader.GetOrdinal("Timestamp")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("Timestamp"))
                                };

                                // Calculate cosine distance between query embedding and result embedding
                                double cosineDistance = CalculateCosineDistance(record.DefinitionEmbedding.ToArray(), queryEmbedding);

                                // Apply temporal decay if enabled
                                if (temporalDecayEnabled && record.Timestamp != DateTime.MinValue)
                                {
                                    double decayMultiplier = ApplyExponentialDecay(record.Timestamp, DateTime.UtcNow, decayImpact);
                                    cosineDistance *= decayMultiplier;
                                    _typedLogger.LogDebug("Applied temporal decay to result {Index}. Original score: {OriginalScore}, New score: {NewScore}",
                                        i, cosineDistance / decayMultiplier, cosineDistance);
                                }

                                EmbeddingSearchResult result = new EmbeddingSearchResult(
                                    i,
                                    cosineDistance,
                                    record.FileName,
                                    record.ChunkIndex,
                                    record.Definition,
                                    embeddingModelName,
                                    record.Timestamp
                                );

                                embeddingsList.Add(result);
                                i++;
                            }

                            // Sort results by cosine similarity (higher values are better)
                            // and take only the requested number of results
                            var sortedResults = embeddingsList
                                .OrderByDescending(r => r.Score)
                                .Take(nResults ?? 5)
                                .Select((result, index) =>
                                {
                                    result.Rank = index + 1;  // Re-assign ranks after sorting
                                    return result;
                                })
                                .ToList();

                            _typedLogger.LogInformation("Fetching image data for {Count} sorted results", sortedResults.Count);

                            // Now fetch image data only for the sorted results we're going to return
                            foreach (var result in sortedResults)
                            {
                                try
                                {
                                    // Construct the blob path based on collection name and filename
                                    string blobPath;

                                    // If the filename already starts with the collection name, use it as is
                                    if (result.FileName.StartsWith($"{collectionName}/", StringComparison.OrdinalIgnoreCase))
                                    {
                                        blobPath = result.FileName;
                                    }
                                    // Otherwise, prepend the collection name
                                    else
                                    {
                                        blobPath = $"{collectionName}/{result.FileName}";
                                    }
                                    _typedLogger.LogInformation("Retrieving image data from blob storage: {BlobPath}", blobPath);

                                    // Download the image to a memory stream
                                    using (var imageStream = new MemoryStream())
                                    {
                                        await _storageManagementService.DownloadBlobToStreamAsync(_storageManagementService.ContainerName, blobPath, imageStream);

                                        // Convert the image data to a Base64 string
                                        byte[] imageBytes = imageStream.ToArray();
                                        string base64Image = Convert.ToBase64String(imageBytes);

                                        // Add the image data to the result
                                        result.ImageData = base64Image;
                                        _typedLogger.LogInformation("Successfully retrieved image data for {FileName}", result.FileName);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    // Log the error but continue processing other results
                                    _typedLogger.LogWarning(ex, "Failed to retrieve image data for {FileName}: {ErrorMessage}", result.FileName, ex.Message);
                                }
                            }

                            _typedLogger.LogInformation("Returning {Count} results sorted by cosine similarity (descending)", sortedResults.Count);
                            return sortedResults;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _typedLogger.LogError(ex, "An error occurred during text-to-image search: {ErrorMessage}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Generate an embedding for text using the CLIP model
        /// </summary>
        /// <param name="text">Text input to generate embedding for</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The embedding vector as a float array</returns>
        private async Task<float[]> GenerateClipTextEmbeddingAsync(string text, CancellationToken cancellationToken)
        {
            try
            {
                // Validate CLIP model configuration
                if (string.IsNullOrEmpty(_clipModelEndpoint) || string.IsNullOrEmpty(_clipModelEndpointApiKey))
                {
                    throw new InvalidOperationException("CLIP model endpoint or API key is not configured. Check environment variables CLIP_MODEL_ENDPOINT and CLIP_MODEL_ENDPOINT_API_KEY.");
                }

                // var client = _httpClientFactory.CreateClient("CustomClient"); // "CustomClient" is configured in Program.cs

                // // Now set the endpoint and auth on that client
                // client.BaseAddress = new Uri(_clipModelEndpoint);
                // client.DefaultRequestHeaders.Authorization =
                //     new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _clipModelEndpointApiKey);
                // client.DefaultRequestHeaders.Accept.Clear();
                // client.DefaultRequestHeaders.Accept.Add(
                //     new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                // Check SSL verification setting
                var sslVerify = Environment.GetEnvironmentVariable("SSL_VERIFY");
                bool disableSslVerification = !string.IsNullOrEmpty(sslVerify) && sslVerify.Equals("False", StringComparison.OrdinalIgnoreCase);

                using var client = disableSslVerification
                    ? new HttpClient(new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                    })
                    : _httpClientFactory.CreateClient("CustomClient");

                // Now set the endpoint and auth on that client
                client.BaseAddress = new Uri(_clipModelEndpoint);
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _clipModelEndpointApiKey);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));


                // Create request body with the text input
                var requestBody = new
                {
                    input_data = new
                    {
                        columns = new[] { "image", "text" },
                        index = new[] { 0 },
                        data = new[]
                        {
                            new object[] { "", text }
                        }
                    },
                    @params = new { }
                };

                string jsonRequest = System.Text.Json.JsonSerializer.Serialize(requestBody);

                // need the using clause here because StringContent implements IDisposable
                using var content = new StringContent(jsonRequest, System.Text.Encoding.UTF8, "application/json");

                _logger.LogInformation("Sending text request to CLIP model endpoint: {DeploymentName}", _clipModelDeploymentName);

                HttpResponseMessage response = await client.PostAsync(string.Empty, content, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    string result = await response.Content.ReadAsStringAsync(cancellationToken);
                    _typedLogger.LogInformation("Successfully received response from CLIP model"); // TODO: visit this log level, name isn't intuitive

                    // Parse the response to extract the embedding
                    _typedLogger.LogDebug("Raw response from CLIP model: {Response}", result);

                    // The response is a list of dictionaries, with the embedding under the "text_features" key
                    var responseObject = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(result);

                    if (responseObject != null && responseObject.Count > 0)
                    {
                        _typedLogger.LogDebug("Response keys: {Keys}", string.Join(", ", responseObject[0].Keys));

                        if (responseObject[0].TryGetValue("text_features", out var embeddingObj) && embeddingObj != null)
                        {
                            // Convert the embedding to a float array
                            var embeddingStr = embeddingObj.ToString();
                            if (!string.IsNullOrEmpty(embeddingStr))
                            {
                                var embeddingArray = System.Text.Json.JsonSerializer.Deserialize<float[]>(embeddingStr);
                                if (embeddingArray != null)
                                {
                                    _typedLogger.LogInformation("Successfully extracted text embedding with {Count} dimensions", embeddingArray.Length);
                                    return embeddingArray;
                                }
                            }
                        }
                    }

                    _typedLogger.LogError("CLIP model response did not contain a valid text_features field. Response: {Response}", result);
                    throw new InvalidOperationException("CLIP model response did not contain a valid text_features field.");
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    _typedLogger.LogError("CLIP model request failed with status code {StatusCode}: {ErrorContent}",
                        response.StatusCode, errorContent);

                    throw new InvalidOperationException($"CLIP model request failed with status code {response.StatusCode}: {errorContent}");
                }
            }
            catch (Exception ex)
            {
                _typedLogger.LogError(ex, "Error generating CLIP text embedding: {ErrorMessage}", ex.Message);
                throw new InvalidOperationException($"Failed to generate CLIP text embedding: {ex.Message}", ex);
            }
        }

        // /// <returns>Dictionary with operation results</returns>
        // private async Task<Dictionary<string, object>> ProcessImageFileAsync(
        //     string collectionName,
        //     string filePath,
        //     string fileName,
        //     bool deleteTempFile,
        //     CancellationToken cancellationToken)
        // {
        //     var resultsDictionary = new Dictionary<string, object>
        //     {
        //         { "success", (bool?)null },
        //         { "message", (string?)null },
        //         { "file_name", fileName },
        //         { "file_path", filePath }
        //     };

        //     try
        //     {
        //         // Determine file type from the file extension
        //         string extension = Path.GetExtension(filePath).ToLowerInvariant();
        //         FileType fileType = extension switch
        //         {
        //             ".jpg" => FileType.Jpeg,
        //             ".jpeg" => FileType.Jpeg,
        //             ".png" => FileType.Png,
        //             ".bmp" => FileType.Bmp,
        //             _ => throw new NotSupportedException($"File extension {extension} is not supported for image embedding.")
        //         };

        //         // Get the appropriate loader from the factory
        //         IFileLoader fileLoader = FileLoaderFactory.GetLoader(fileType);
        //         object rawData = fileLoader.LoadFile(filePath);
        //         _typedLogger.LogInformation("Image data load complete for file {FileName}", fileName);

        //         // Get the appropriate parser from the factory
        //         IFileParser fileParser = FileParserFactory.GetParser(fileType, _kernel);

        //         // Parse the input file to get a caption
        //         FileParsingResult parsingResult = await fileParser.Parse(rawData);
        //         string imageCaption = parsingResult.parsedContent;
        //         _typedLogger.LogInformation("Image parser complete for file {FileName}", fileName);

        //         // Get the CLIP embedding service
        //         var clipEmbeddingService = _kernel.GetRequiredService<ITextEmbeddingGenerationService>(_imageEmbeddingServiceId);

        //         if (clipEmbeddingService == null)
        //         {
        //             throw new InvalidOperationException("CLIP embedding service is not properly configured.");
        //         }

        //         // Generate embedding for the image caption
        //         var embedding = await clipEmbeddingService.GenerateEmbeddingAsync(imageCaption, cancellationToken);

        //         // Create a record for the image
        //         var record = new EmbeddingChunkRecord
        //         {
        //             Key = Guid.NewGuid(),
        //             FileName = fileName,
        //             PageId = 0,
        //             ChunkIndex = 0,
        //             Definition = imageCaption,
        //             EmbeddingServiceId = _imageEmbeddingServiceId,
        //             DefinitionEmbedding = embedding,
        //             Timestamp = DateTime.UtcNow,
        //             Modality = "ImageEmbedding"
        //         };

        //         // Get the vector store and collection
        //         var vectorStore = _kernel.GetRequiredService<IVectorStore>();
        //         var collection = vectorStore.GetCollection<Guid, EmbeddingChunkRecord>(collectionName);

        //         // Upsert the record into the collection
        //         var options = new UpsertRecordOptions();
        //         await collection.UpsertAsync(record, options, cancellationToken);

        //         // Clean up temporary file if needed
        //         if (deleteTempFile && File.Exists(filePath))
        //         {
        //             try
        //             {
        //                 File.Delete(filePath);
        //                 _typedLogger.LogInformation("Deleted temporary file: {TempFilePath}", filePath);
        //             }
        //             catch (Exception ex)
        //             {
        //                 _typedLogger.LogWarning(ex, "Failed to delete temporary file: {TempFilePath}", filePath);
        //             }
        //         }

        //         _typedLogger.LogInformation("Successfully ingested image file '{FileName}' into collection '{CollectionName}'.",
        //             fileName, collectionName);

        //         resultsDictionary["success"] = true;
        //         resultsDictionary["message"] = $"Successfully ingested image file {fileName} into collection {collectionName}.";
        //         resultsDictionary["caption"] = imageCaption;

        //         return resultsDictionary;
        //     }
        //     catch (Exception ex)
        //     {
        //         _typedLogger.LogError(ex, "An error occurred while processing image file {FileName}", fileName);
        //         resultsDictionary["success"] = false;
        //         resultsDictionary["message"] = $"ProcessImageFileAsync error: {ex.Message}";
        //         return resultsDictionary;
        //     }
        // }

        /// <summary>
        /// Preprocess an image for the CLIP model by resizing it to 336x335 pixels
        /// </summary>
        /// <param name="inputPath">Path to the input image</param>
        /// <param name="outputPath">Path where the preprocessed image will be saved</param>
        /// <returns>Task representing the asynchronous operation</returns>
        private async Task PreprocessImageForClipAsync(string inputPath, string outputPath)
        {
            try
            {
                // Use SkiaSharp for cross-platform image processing
                using (var inputStream = File.OpenRead(inputPath))
                using (var original = SKBitmap.Decode(inputStream))
                {
                    // Create a new bitmap with the target dimensions (336x335)
                    var resizedImage = original.Resize(new SKImageInfo(336, 335), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Nearest));

                    // Convert to image for saving
                    using (var image = SKImage.FromBitmap(resizedImage))
                    using (var outputStream = File.OpenWrite(outputPath))
                    {
                        // Encode as PNG and save to the output path
                        image.Encode(SKEncodedImageFormat.Png, 100).SaveTo(outputStream);
                    }
                }

                await Task.CompletedTask; // To make this method async consistent
            }
            catch (Exception ex)
            {
                _typedLogger.LogError(ex, "Error preprocessing image for CLIP model: {ErrorMessage}", ex.Message);
                throw new InvalidOperationException($"Failed to preprocess image for CLIP model: {ex.Message}", ex);
            }
        }

        // /// <summary>
        // /// Generate an embedding for an image using the CLIP model
        // /// </summary>
        // /// <param name="imagePath">Path to the preprocessed image</param>
        // /// <param name="cancellationToken">Cancellation token</param>
        // /// <returns>The embedding vector as a float array</returns>
        // private async Task<float[]> GenerateClipImageEmbeddingAsync(string imagePath, CancellationToken cancellationToken)
        // {
        //     try
        //     {
        //         // Validate CLIP model configuration
        //         if (string.IsNullOrEmpty(_clipModelEndpoint) || string.IsNullOrEmpty(_clipModelEndpointApiKey))
        //         {
        //             throw new InvalidOperationException("CLIP model endpoint or API key is not configured. Check environment variables CLIP_MODEL_ENDPOINT and CLIP_MODEL_ENDPOINT_API_KEY.");
        //         }

        //         // Create HTTP client with certificate validation handling
        //         var handler = new HttpClientHandler()
        //         {
        //             ClientCertificateOptions = ClientCertificateOption.Manual,
        //             ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) => true
        //         };

        //         using (var client = new HttpClient(handler))
        //         {
        //             // Read the image file as bytes
        //             byte[] imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        //             string base64Image = Convert.ToBase64String(imageBytes);

        //             // Create request body with the base64-encoded image
        //             var requestBody = new
        //             {
        //                 input_data = new
        //                 {
        //                     columns = new[] { "image", "text" },
        //                     index = new[] { 0 },
        //                     data = new[]
        //                     {
        //                         new object[] { base64Image, "" }
        //                     }
        //                 },
        //                 @params = new { }
        //             };

        //             // Convert request body to JSON
        //             string jsonRequest = System.Text.Json.JsonSerializer.Serialize(requestBody);

        //             // Set up the HTTP client
        //             client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _clipModelEndpointApiKey);
        //             client.BaseAddress = new Uri(_clipModelEndpoint);
        //             client.DefaultRequestHeaders.Add("Accept", "application/json");

        //             // Create content for the request
        //             var content = new StringContent(jsonRequest);
        //             content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        //             // Make the request to the CLIP model endpoint
        //             _typedLogger.LogInformation("Sending request to CLIP model endpoint: {DeploymentName}", _clipModelDeploymentName);
        //             HttpResponseMessage response = await client.PostAsync("", content, cancellationToken);

        //             if (response.IsSuccessStatusCode)
        //             {
        //                 string result = await response.Content.ReadAsStringAsync(cancellationToken);
        //                 _typedLogger.LogInformation("Successfully received response from CLIP model");

        //                 // Parse the response to extract the embedding
        //                 _typedLogger.LogDebug("Raw response from CLIP model: {Response}", result);

        //                 // The response is a list of dictionaries, with the embedding under the "image_features" key
        //                 var responseObject = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(result);

        //                 if (responseObject != null && responseObject.Count > 0)
        //                 {
        //                     _typedLogger.LogDebug("Response keys: {Keys}", string.Join(", ", responseObject[0].Keys));

        //                     if (responseObject[0].TryGetValue("image_features", out var embeddingObj) && embeddingObj != null)
        //                     {
        //                         // Convert the embedding to a float array
        //                         var embeddingStr = embeddingObj.ToString();
        //                         if (!string.IsNullOrEmpty(embeddingStr))
        //                         {
        //                             var embeddingArray = System.Text.Json.JsonSerializer.Deserialize<float[]>(embeddingStr);
        //                             if (embeddingArray != null)
        //                             {
        //                                 _typedLogger.LogInformation("Successfully extracted embedding with {Count} dimensions", embeddingArray.Length);
        //                                 // TODO: Explore batched embeddings by processing multiple items in the responseObject list
        //                                 return embeddingArray;
        //                             }
        //                         }
        //                     }
        //                 }

        //                 _typedLogger.LogError("CLIP model response did not contain a valid image_features field. Response: {Response}", result);
        //                 throw new InvalidOperationException("CLIP model response did not contain a valid image_features field.");
        //             }
        //             else
        //             {
        //                 string errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
        //                 _typedLogger.LogError("CLIP model request failed with status code {StatusCode}: {ErrorContent}", 
        //                     response.StatusCode, errorContent);

        //                 throw new InvalidOperationException($"CLIP model request failed with status code {response.StatusCode}: {errorContent}");
        //             }
        //         }
        //     }
        //     catch (Exception ex)
        //     {
        //         _typedLogger.LogError(ex, "Error generating CLIP embedding: {ErrorMessage}", ex.Message);
        //         throw new InvalidOperationException($"Failed to generate CLIP embedding: {ex.Message}", ex);
        //     }
        // }
        /// <summary>
        /// Generate an embedding for an image using the CLIP model
        /// </summary>
        /// <param name="imagePath">Path to the preprocessed image</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The embedding vector as a float array</returns>
        private async Task<float[]> GenerateClipImageEmbeddingAsync(string imagePath, CancellationToken cancellationToken)
        {
            try
            {
                // Validate CLIP model configuration
                if (string.IsNullOrEmpty(_clipModelEndpoint) || string.IsNullOrEmpty(_clipModelEndpointApiKey))
                    throw new InvalidOperationException(
                        "CLIP model endpoint or API key is not configured. " +
                        "Check environment variables CLIP_MODEL_ENDPOINT and CLIP_MODEL_ENDPOINT_API_KEY.");

                // // Obtain a preconfigured HttpClient from DI
                // var client = _httpClientFactory.CreateClient("CustomClient");
                // client.BaseAddress = new Uri(_clipModelEndpoint);
                // client.DefaultRequestHeaders.Authorization =
                //     new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _clipModelEndpointApiKey);
                // client.DefaultRequestHeaders.Accept.Clear();
                // client.DefaultRequestHeaders.Accept.Add(
                //     new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                // Check SSL verification setting
                var sslVerify = Environment.GetEnvironmentVariable("SSL_VERIFY");
                bool disableSslVerification = !string.IsNullOrEmpty(sslVerify) && sslVerify.Equals("False", StringComparison.OrdinalIgnoreCase);

                using var client = disableSslVerification
                    ? new HttpClient(new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                    })
                    : _httpClientFactory.CreateClient("CustomClient");

                client.BaseAddress = new Uri(_clipModelEndpoint);
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _clipModelEndpointApiKey);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                // Read and encode the image
                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
                string base64Image = Convert.ToBase64String(imageBytes);

                // Build request payload
                var requestBody = new
                {
                    input_data = new
                    {
                        columns = new[] { "image", "text" },
                        index = new[] { 0 },
                        data = new[]
                        {
                            new object[] { base64Image, "" }
                        }
                    },
                    @params = new { }
                };
                string jsonRequest = System.Text.Json.JsonSerializer.Serialize(requestBody);

                using var content = new StringContent(jsonRequest, System.Text.Encoding.UTF8, "application/json");

                _typedLogger.LogInformation(
                    "Sending image request to CLIP model endpoint: {DeploymentName}",
                    _clipModelDeploymentName);

                HttpResponseMessage response =
                    await client.PostAsync(string.Empty, content, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    string result = await response.Content.ReadAsStringAsync(cancellationToken);
                    _typedLogger.LogInformation("Successfully received response from CLIP model");
                    _typedLogger.LogDebug("Raw response from CLIP model: {Response}", result);

                    // Deserialize and extract the embedding
                    var responseList = System.Text.Json.JsonSerializer
                        .Deserialize<List<Dictionary<string, object>>>(result);

                    if (responseList?.Count > 0
                        && responseList[0].TryGetValue("image_features", out var featObj)
                        && featObj != null)
                    {
                        string featJson = featObj.ToString();
                        var embedding = System.Text.Json.JsonSerializer.Deserialize<float[]>(featJson);
                        if (embedding != null)
                        {
                            _typedLogger.LogInformation(
                                "Successfully extracted embedding with {Count} dimensions",
                                embedding.Length);
                            return embedding;
                        }
                    }

                    _typedLogger.LogError(
                        "CLIP model response did not contain a valid image_features field. Response: {Response}",
                        result);
                    throw new InvalidOperationException(
                        "CLIP model response did not contain a valid image_features field.");
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    _typedLogger.LogError(
                        "CLIP model request failed with status code {StatusCode}: {ErrorContent}",
                        response.StatusCode, errorContent);
                    throw new InvalidOperationException(
                        $"CLIP model request failed with status code {response.StatusCode}: {errorContent}");
                }
            }
            catch (Exception ex)
            {
                _typedLogger.LogError(ex,
                    "Error generating CLIP image embedding: {ErrorMessage}", ex.Message);
                throw new InvalidOperationException(
                    $"Failed to generate CLIP image embedding: {ex.Message}", ex);
            }
        }
    }
}
