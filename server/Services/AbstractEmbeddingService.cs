using Microsoft.SemanticKernel;
using PgVectorDynamicRAG.Data;
using Microsoft.Extensions.VectorData;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Npgsql;

namespace PgVectorDynamicRAG.Services
{
    /// <summary>
    /// Abstract base class that defines common functionality for embedding services.
    /// This class provides a foundation for both text and image embedding services.
    /// </summary>
    public abstract class AbstractEmbeddingService
    {
        /// <summary>
        /// The Semantic Kernel instance
        /// </summary>
        protected readonly Kernel _kernel;
        
        /// <summary>
        /// Logger instance
        /// </summary>
        protected readonly ILogger<object> _logger;
        
        /// <summary>
        /// Postgres connection factory
        /// </summary>
        protected readonly IPostgresConnectionFactory _connectionFactory;
        
        /// <summary>
        /// Schema management service
        /// </summary>
        protected readonly SchemaManagementService _schemaManagementService;
        
        /// <summary>
        /// Storage management service
        /// </summary>
        protected readonly AbstractStorageService _storageManagementService;
        
        /// <summary>
        /// Semaphore to limit concurrent embedding API calls
        /// </summary>
        protected readonly SemaphoreSlim _embeddingSemaphore;

        /// <summary>
        /// Constructor for the abstract embedding service.
        /// </summary>
        /// <param name="kernel">The Semantic Kernel instance</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="connectionFactory">Postgres connection factory</param>
        /// <param name="schemaManagementService">Schema management service</param>
        /// <param name="embeddingModelService">Embedding model service</param>
        /// <param name="storageManagementService">Storage management service</param>
        protected AbstractEmbeddingService(
            Kernel kernel,
            ILogger<object> logger,
            IPostgresConnectionFactory connectionFactory,
            SchemaManagementService schemaManagementService,
            AbstractStorageService storageManagementService)
        {
            _kernel = kernel;
            _logger = logger;
            _connectionFactory = connectionFactory;
            _schemaManagementService = schemaManagementService;
            _storageManagementService = storageManagementService;
            _embeddingSemaphore = new SemaphoreSlim(int.TryParse(Environment.GetEnvironmentVariable("CHUNKING_PARALLELISM"), out var parallelism) ? parallelism : 1);
        }

        /// <summary>
        /// Ingest an uploaded file into a collection.
        /// </summary>
        /// <param name="collectionName">Name of the collection to ingest into</param>
        /// <param name="chunkSize">Size of chunks in words</param>
        /// <param name="chunkOverlapFraction">Fraction of chunk size to overlap</param>
        /// <param name="file">File to ingest</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary with operation results</returns>private readonly SemaphoreSlim _embeddingSemaphore = new SemaphoreSlim(int.TryParse(Environment.GetEnvironmentVariable("CHUNKING_PARALLELISM"), out var parallelism) ? parallelism : 1);
        public abstract Task<Dictionary<string, object>> IngestUploadedFileAsync(
            string collectionName,
            int chunkSize,
            float chunkOverlapFraction,
            IFormFile file,
            bool saveToBlobStorage = false,
            CancellationToken cancellationToken = default);

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
        public abstract Task<Dictionary<string, object>> IngestLocalFileAsync(
            string collectionName,
            int chunkSize,
            float chunkOverlapFraction,
            string filePath,
            bool saveToBlobStorage = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Ingest text content into a collection.
        /// </summary>
        /// <param name="collectionName">Name of the collection to ingest into</param>
        /// <param name="chunkSize">Size of chunks in words</param>
        /// <param name="chunkOverlapFraction">Fraction of chunk size to overlap</param>
        /// <param name="content">Content to ingest</param>
        /// <param name="fileName">Name to associate with the content</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary with operation results</returns>
        public abstract Task<Dictionary<string, object>> NewTextMemoryAsync(
            string collectionName,
            int chunkSize,
            float chunkOverlapFraction,
            string content,
            bool saveToBlobStorage,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Remove a file from a collection.
        /// </summary>
        /// <param name="fileName">Name of the file to remove</param>
        /// <param name="collectionName">Name of the collection</param>
        /// <param name="isBlobFile">Flag indicating if the file is in Azure Blob Storage</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary with operation results</returns>
        // public abstract Task<Dictionary<string, object>> RemoveFileFromCollectionAsync(
        //     string filePath,
        //     string collectionName,
        //     bool isBlobFile = false,
        //     CancellationToken cancellationToken = default);

        /// <summary>
        /// Remove a file from a collection.
        /// </summary>
        /// <param name="filePath">Path to the file to remove</param>
        /// <param name="collectionName">Name of the collection</param>
        /// <param name="isBlobFile">Flag indicating if the file is in Azure Blob Storage</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary with operation results</returns>
        public virtual async Task<Dictionary<string, object>> RemoveFileFromCollectionAsync(
            string filePath,
            string collectionName,
            bool isBlobFile = false,
            CancellationToken cancellationToken = default)
        {

            var resultsDictionary = new Dictionary<string, object>
            {
                { "success", (bool?)null },
                { "message", (string?)null }
            };

            try {

                // Get the vector store from the kernel’s DI container.
                var vectorStore = _kernel.GetRequiredService<VectorStore>();

                if (vectorStore == null)
                {
                    throw new InvalidOperationException("RemoveFileFromCollectionAsync error: vector store is not configured in DI.");
                }

                // If this is a blob file, also delete it from blob storage
                if (isBlobFile)
                {
                    _logger.LogInformation("Deleting blob file: {relativeFilePath} from collection: {CollectionName}", filePath, collectionName);
                    
                    // Delete the blob from storage
                    // relativeFilePath = "/Test/test_file.txt"
                    string blobRelativePath = Path.Join("/", filePath);
                    var status = await _storageManagementService.DeleteSingleBlob(blobRelativePath);
                    if (status.TryGetValue("deleted", out var deleted) && status.TryGetValue("pathInContainer", out var pathInContainer))
                    {
                        _logger.LogInformation("Successfully ({Deleted}) deleted blob: {BlobPath}", deleted, pathInContainer);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to retrieve deletion status or path for blob: {BlobRelativePath}", blobRelativePath);
                    }
                }

                // Retrieve the collection (assuming FileChunkRecord is your record type).
                var collection = vectorStore.GetCollection<Guid, EmbeddingChunkRecord>(collectionName);

                // Check if the collection exists.
                if (!await collection.CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException($"RemoveFileFromCollectionAsync error: collection {collectionName} does not exist.");
                }

                _logger.LogInformation("Searching for records in {CollectionName} with file name: {FilePath}...", collectionName, filePath);

                // Use the new GetAllRecordsAsync method to iterate over records.
                var keysToDelete = new List<Guid>();

                // Use a SQL query to get keys where the "relativeFilePath" column equals the relativeFilePath parameter.
                var sql = $@"
                    SELECT ""Key""
                    FROM ""{collectionName}""
                    WHERE ""FileName"" = @fileName";

                // Assuming _connectionString is available in your class (e.g., injected via configuration)
                bool anyRecordFlag = false;
                var fileNameOnly = Path.GetFileName(filePath);
                await using (NpgsqlConnection conn = _connectionFactory.CreateConnection())
                {
                    await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                    await using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        // Extract just the file name (remove all path information)
                        cmd.Parameters.AddWithValue("fileName", fileNameOnly);

                        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            {
                                // Assuming the "Key" column is stored as a UUID.
                                var key = reader.GetGuid(0);
                                keysToDelete.Add(key);
                                anyRecordFlag = true;
                            }
                        }
                    }
                }

                if (anyRecordFlag == false) {
                    throw new InvalidOperationException($"No record for filename {fileNameOnly} exist for collection {collectionName}.");
                }

                await collection.DeleteAsync(keysToDelete, cancellationToken);

                resultsDictionary["success"] = true;
                resultsDictionary["message"] = $"Successfully removed all records for file: {filePath} from collection: {collectionName}";
                return resultsDictionary;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while removing file from collection.");
                resultsDictionary["success"] = false;
                resultsDictionary["message"] = $"RemoveFileFromCollectionAsync error: {ex.Message}";
                return resultsDictionary;
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
        public abstract Task<List<EmbeddingSearchResult>> TextVectorSearchFixedCollectionAsync(
            string query,
            string collectionName,
            int? nResults = 5,
            bool timeWindowEnabled = false,
            int windowDays = 30,
            bool temporalDecayEnabled = false,
            double decayImpact = 0.01,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Perform a semantic search on a collection.
        /// </summary>
        /// <param name="query">Query string</param>
        /// <param name="collectionName">Name of the collection to search</param>
        /// <param name="nResults">Number of results to return</param>
        /// <param name="timeWindowEnabled">Whether to enable time-window filtering</param>
        /// <param name="windowDays">Number of days to include in the time window</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of search results</returns>
        public abstract Task<List<EmbeddingSearchResult>> TextSemanticSearchFixedCollectionAsync(
            string query,
            string collectionName,
            int? nResults = 5,
            bool timeWindowEnabled = false,
            int windowDays = 30,
            bool temporalDecayEnabled = false,
            double decayImpact = 0.01,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Abstract method for text-embedding search on both TextEmbedding Collections and ImageEmbeddings
        /// collections. These 
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
        public abstract Task<List<EmbeddingSearchResult>> TextSearchFixedCollectionAsync(
            string query,
            string collectionName,
            int? nResults = 5,
            double alpha = 0.75,
            bool timeWindowEnabled = false,
            int windowDays = 30,
            bool temporalDecayEnabled = false,
            double decayImpact = 0.01,
            CancellationToken cancellationToken = default);

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
        public abstract Task<List<EmbeddingSearchResult>> ImageSearchFixedCollectionAsync(
            IFormFile query,
            string collectionName,
            int? nResults = 5,
            bool? timeWindowEnabled = false,
            int? windowDays = 30,
            bool? temporalDecayEnabled = false,
            double? decayImpact = 0.01,
            CancellationToken cancellationToken = default);


        /// <summary>
        /// Save a file to blob storage.
        /// </summary>
        /// <param name="collectionName">Name of the collection</param>
        /// <param name="file">File to save</param>
        /// <returns>Dictionary with operation results</returns>
        protected async Task SaveFileToBlobStorageAsync(string filePath, string blobPath)
        {
            _logger.LogInformation("Saving file to storage: {BlobPath}", blobPath);
            
            try
            {
                // Read the file into a stream and use the abstract storage service
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    string contentType = "application/octet-stream"; // Default content type
                    
                    // Try to determine content type from file extension
                    string extension = Path.GetExtension(filePath).ToLowerInvariant();
                    contentType = extension switch
                    {
                        ".txt" => "text/plain",
                        ".pdf" => "application/pdf",
                        ".json" => "application/json",
                        ".csv" => "text/csv",
                        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                        ".jpg" or ".jpeg" => "image/jpeg",
                        ".png" => "image/png",
                        ".gif" => "image/gif",
                        ".bmp" => "image/bmp",
                        ".tiff" or ".tif" => "image/tiff",
                        _ => "application/octet-stream"
                    };
                    
                    await _storageManagementService.SaveFileStreamToStorage(blobPath, fileStream, contentType);
                }
                
                _logger.LogInformation("Successfully saved file to storage: {BlobPath}", blobPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save file to storage: {BlobPath}", blobPath);
                throw;
            }
        }

        /// <summary>
        /// Download a blob to a temporary file.
        /// </summary>
        /// <param name="collectionName">Collection name (used as the first folder in blob path)</param>
        /// <param name="blobRelativePath">Relative path to the blob</param>
        /// <param name="tempFilePath">Path where the temporary file should be saved</param>
        /// <returns></returns>
        protected async Task DownloadBlobToTempFileAsync(string collectionName, string blobRelativePath, string tempFilePath)
        {
            try
            {
                // Use BlobManagementService to download the blob to a temporary file
                using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                {
                    // Download blob directly to the file stream
                    await _storageManagementService.DownloadBlobToStreamAsync(collectionName, blobRelativePath, fileStream);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download blob {CollectionName}/{BlobPath} to temporary file {TempFilePath}", 
                    collectionName, blobRelativePath, tempFilePath);
                throw;
            }
        }

        /// <summary>
        /// Check if a collection exists and has the expected modality.
        /// </summary>
        /// <param name="collectionName">Name of the collection to check</param>
        /// <param name="expectedModality">Expected modality of the collection</param>
        /// <returns>True if the collection exists and has the expected modality</returns>
        protected async Task<bool> ValidateCollectionModalityAsync(string collectionName, string expectedModality)
        {
            try
            {
                bool collectionExists = await _schemaManagementService.CheckCollectionExists(collectionName);
                if (!collectionExists)
                {
                    _logger.LogError($"Collection named {collectionName} does not exist.");
                    return false;
                }

                string embeddingServiceId;
                string embeddingServiceModality;
                (embeddingServiceId, embeddingServiceModality) = await _schemaManagementService.getDefaultEmbeddingServiceDetails(collectionName);

                if (embeddingServiceModality != expectedModality)
                {
                    _logger.LogError($"Collection {collectionName} has modality {embeddingServiceModality}, but expected {expectedModality}.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating collection modality for {collectionName}");
                return false;
            }
        }

        /// <summary>
        /// Calculates the cosine distance between two vectors.
        /// </summary>
        /// <param name="vector1">First vector</param>
        /// <param name="vector2">Second vector</param>
        /// <returns>Cosine distance between the two vectors</returns>
        protected static double CalculateCosineDistance(float[] vector1, float[] vector2)
        {
            double dotProduct = 0;
            double magnitude1 = 0;
            double magnitude2 = 0;

            for (int i = 0; i < vector1.Length; i++)
            {
                dotProduct += vector1[i] * vector2[i];
                magnitude1 += Math.Pow(vector1[i], 2);
                magnitude2 += Math.Pow(vector2[i], 2);
            }

            magnitude1 = Math.Sqrt(magnitude1);
            magnitude2 = Math.Sqrt(magnitude2);

            if (magnitude1 != 0 && magnitude2 != 0)
            {
                return dotProduct / (magnitude1 * magnitude2);
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// Applies exponential decay to a similarity score based on timestamp.
        /// </summary>
        /// <param name="itemTimestamp">Timestamp of the item</param>
        /// <param name="referenceTimestamp">Reference timestamp (usually current time)</param>
        /// <param name="decayRate">Decay rate factor</param>
        /// <returns>Decay factor to multiply with similarity score</returns>
        public double ApplyExponentialDecay(DateTime itemTimestamp, DateTime referenceTimestamp, double decayRate)
        {
            double daysDifference = (referenceTimestamp - itemTimestamp).TotalDays;

            // Exponential decay formula
            double result = Math.Exp(-decayRate * daysDifference);
            
            return result;
        }
    }
}
