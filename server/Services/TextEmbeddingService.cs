using Microsoft.SemanticKernel;
using Microsoft.Extensions.VectorData;
using PgVectorDynamicRAG.Data;
using PgVectorDynamicRAG.Factories;
using Microsoft.SemanticKernel.Embeddings;       // <-- For ITextEmbeddingGenerationService
using Npgsql;
using Pgvector;
using Microsoft.Extensions.AI;

#pragma warning disable SKEXP0001

namespace PgVectorDynamicRAG.Services
{
    /// <summary>
    /// Service for handling text-based embeddings and retrieval.
    /// This service specializes in processing text files, generating text embeddings,
    /// and performing text-based searches.
    /// </summary>
    public class TextEmbeddingService : AbstractEmbeddingService
    {
        private readonly ILogger<TextEmbeddingService> _typedLogger;

        /// <summary>
        /// Constructor for the text embedding service.
        /// </summary>
        /// <param name="kernel">The Semantic Kernel instance</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="connectionFactory">Postgres connection factory</param>
        /// <param name="schemaManagementService">Schema management service</param>
        /// <param name="blobManagementService">Blob management service</param>
        public TextEmbeddingService(
            Kernel kernel,
            ILogger<TextEmbeddingService> logger,
            IPostgresConnectionFactory connectionFactory,
            SchemaManagementService schemaManagementService,
            AbstractStorageService storageManagementService)
            : base(kernel, (ILogger<object>)(object)logger, connectionFactory, schemaManagementService, storageManagementService)
        {
            _typedLogger = logger;
        }

        /// 
        /// Common file processing logic shared between local and uploaded files
        /// 
        private async Task<Dictionary<string, object>> ProcessFileAsync(
            string collectionName,
            int chunkSize,
            float chunkOverlapFraction,
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

            try
            {
                // Collection identification
                _logger.LogInformation("Collection identification starting [1/4]");
                bool collectionExists = await _schemaManagementService.CheckCollectionExists(collectionName);
                string embeddingServiceId;
                string embeddingServiceModality;                
                
                if (!collectionExists)
                {
                    throw new InvalidOperationException($"Collection named {collectionName} does not exists.");
                }
                else
                {
                    (embeddingServiceId, embeddingServiceModality) = await _schemaManagementService.getDefaultEmbeddingServiceDetails(collectionName);
                }
                _logger.LogInformation("Collection identification complete [step 1 of 4]");

                // Loader: Determine file type from the file extension.
                string extension = Path.GetExtension(filePath).ToLowerInvariant();
                FileType fileType = extension switch
                {
                    ".json" => FileType.Json,
                    ".csv"  => FileType.Csv,
                    ".docx" => FileType.Docx,
                    ".pdf"  => FileType.Pdf,
                    ".txt"  => FileType.Txt,
                    ".xlsx" => FileType.Xlsx,
                    ".xml"  => FileType.Xml,
                    ".png"  => FileType.Png,
                    ".jpeg" => FileType.Jpeg,
                    ".jpg"  => FileType.Jpeg,
                    ".bmp"  => FileType.Bmp,
                    _ => throw new NotSupportedException($"File extension {extension} is not supported for text embeddings collections.")
                };

                // Loader: Get the appropriate loader from the factory.
                IFileLoader fileLoader = FileLoaderFactory.GetLoader(fileType);
                object rawData = fileLoader.LoadFile(filePath);
                _logger.LogInformation("Data load complete [step 2 of 4]");

                // Parses the input data into metadata (embedding preprocessing)
                _logger.LogInformation("File type: {fileType}", fileType);
                IFileParser fileParser = FileParserFactory.GetParser(fileType, _kernel);
                
                // Parser: parse the input file
                FileParsingResult parsingResult = await fileParser.Parse(rawData);
                FileChunkingResult chunkingResult = await fileParser.Chunk(parsingResult, fileName, chunkSize, chunkOverlapFraction);
                _logger.LogInformation("Parser complete [step 3 of 4]");
                
                // Embedder
                var vectorStore = _kernel.GetRequiredService<VectorStore>();
                var collection = vectorStore.GetCollection<Guid, EmbeddingChunkRecord>(collectionName);

                IEmbeddingGenerator<string, Embedding<float>> textEmbeddingService;
                try
                {
                    textEmbeddingService = _kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(embeddingServiceId.ToString());
                }
                catch
                {
                    var legacyService = _kernel.GetRequiredService<ITextEmbeddingGenerationService>(embeddingServiceId.ToString());
                    textEmbeddingService = legacyService.AsEmbeddingGenerator();
                }

                if (textEmbeddingService == null)
                {
                    throw new InvalidOperationException("Embedding service is not properly configured.");
                }

                // Generate embeddings for each chunk's Definition with controlled concurrency
                int totalChunks = chunkingResult.chunkRecords.Count;
                _logger.LogInformation("Generating embeddings for {Count} chunks with max concurrency of {MaxConcurrency}...", 
                    totalChunks, _embeddingSemaphore.CurrentCount);
                    
                int processedCount = 0;
                // Create a cancellation token source that can be used to cancel all tasks
                var cancellationTokenSource = new CancellationTokenSource();
                var embeddingTasksCancellationToken = cancellationTokenSource.Token;
                
                var embeddingTasks = chunkingResult.chunkRecords.Select(async (record, index) =>
                {
                    record.EmbeddingServiceId = embeddingServiceId;
                    record.Timestamp = DateTime.UtcNow;
                    
                    // Exponential back-off parameters
                    int maxRetries = 5;
                    int retryCount = 0;
                    int baseDelayMs = 1000; // Start with 1 second delay
                    bool success = false;
                    
                    // Check if cancellation has been requested before proceeding
                    if (embeddingTasksCancellationToken.IsCancellationRequested)
                    {
                        return; // Skip this task if cancellation was requested
                    }
                    
                    // Wait until a slot is available in the semaphore
                    await _embeddingSemaphore.WaitAsync(embeddingTasksCancellationToken);
                    _logger.LogDebug("Starting embedding generation for chunk {Index}/{Total}", index + 1, totalChunks);
                    
                    try
                    {
                        while (!success && retryCount < maxRetries && !embeddingTasksCancellationToken.IsCancellationRequested)
                        {
                            try
                            {
                                // Check if cancellation was requested
                                if (embeddingTasksCancellationToken.IsCancellationRequested)
                                {
                                    _logger.LogInformation("Embedding generation for chunk {Index}/{Total} was cancelled", index + 1, totalChunks);
                                    break;
                                }
                                
                                await Task.Delay(100, embeddingTasksCancellationToken);
                                var startTime = DateTime.UtcNow;
                                // record.DefinitionEmbedding = await textEmbeddingService.GenerateEmbeddingAsync(record.Definition);
                                var embeddingResult = await textEmbeddingService.GenerateAsync(record.Definition);
                                record.DefinitionEmbedding = embeddingResult.Vector.ToArray();
                                var duration = DateTime.UtcNow - startTime;
                                
                                success = true; // If we get here, the operation succeeded
                                Interlocked.Increment(ref processedCount);
                                
                                _logger.LogInformation(
                                    "Successfully generated embedding for chunk {Index}/{Total} in {Duration}ms (Total completed: {Completed}/{Total})", 
                                    index + 1, totalChunks, duration.TotalMilliseconds, processedCount, totalChunks);
                            }
                            catch (Exception ex) when (ex.Message.Contains("429") || 
                                                    (ex.InnerException != null && ex.InnerException.Message.Contains("429")))
                            {
                                retryCount++;
                                _logger.LogWarning("Rate limit (429) encountered for chunk {Index}/{Total}: {ErrorMessage}", 
                                    index + 1, totalChunks, ex.Message);
                                    
                                if (retryCount >= maxRetries)
                                {
                                    _logger.LogError("Failed to generate embedding for chunk {Index}/{Total} after {RetryCount} attempts due to rate limiting", 
                                        index + 1, totalChunks, retryCount);
                                    // Signal cancellation to all other tasks
                                    cancellationTokenSource.Cancel();
                                    throw new InvalidOperationException($"Maximum retry attempts reached for chunk {index + 1}/{totalChunks}. The embedding service may be experiencing rate limiting issues.");
                                }
                                
                                // Calculate exponential back-off delay with jitter
                                int delayMs = baseDelayMs * (int)Math.Pow(2, retryCount - 1);
                                // Add jitter (random variation) to avoid thundering herd problem
                                delayMs = delayMs + new Random().Next(0, delayMs / 2);
                                
                                _logger.LogWarning("Chunk {Index}/{Total}: Retrying in {DelayMs}ms (attempt {RetryCount}/{MaxRetries})", 
                                    index + 1, totalChunks, delayMs, retryCount, maxRetries);
                                    
                                await Task.Delay(delayMs, embeddingTasksCancellationToken);
                            }
                            catch (Exception ex)
                            {
                                retryCount++;
                                _logger.LogError("Error generating embedding for chunk {Index}/{Total}: {ErrorMessage}", 
                                    index + 1, totalChunks, ex.Message);
                                    
                                if (retryCount >= maxRetries)
                                {
                                    _logger.LogError("Failed to generate embedding for chunk {Index}/{Total} after {RetryCount} attempts", 
                                        index + 1, totalChunks, retryCount);
                                    // Signal cancellation to all other tasks
                                    cancellationTokenSource.Cancel();
                                    throw new InvalidOperationException($"Error generating embedding for chunk {index + 1}/{totalChunks}: {ex.Message}", ex);
                                }
                                
                                // Calculate exponential back-off delay with jitter
                                int delayMs = baseDelayMs * (int)Math.Pow(2, retryCount - 1);
                                // Add jitter (random variation) to avoid thundering herd problem
                                delayMs = delayMs + new Random().Next(0, delayMs / 2);
                                
                                _logger.LogWarning("Chunk {Index}/{Total}: Retrying in {DelayMs}ms (attempt {RetryCount}/{MaxRetries})", 
                                    index + 1, totalChunks, delayMs, retryCount, maxRetries);
                                    
                                await Task.Delay(delayMs, embeddingTasksCancellationToken);
                            }

                        }
                    }
                    finally
                    {
                        // Always release the semaphore, even if an exception occurred
                        _embeddingSemaphore.Release();
                        _logger.LogDebug("Released semaphore for chunk {Index}/{Total}. Available slots: {AvailableSlots}", 
                            index + 1, totalChunks, _embeddingSemaphore.CurrentCount);
                    }
                });
                
                try
                {
                    // Use Task.WhenAny to detect the first task that completes (successfully or with an exception)
                    var pendingTasks = embeddingTasks.ToList();
                    
                    while (pendingTasks.Count > 0 && !cancellationTokenSource.IsCancellationRequested)
                    {                    
                        // Wait for any task to complete
                        Task completedTask = await Task.WhenAny(pendingTasks);
                        pendingTasks.Remove(completedTask);
                        
                        try
                        {                        
                            // This will throw if the task failed
                            await completedTask;
                        }
                        catch (Exception ex)
                        {                        
                            // If any task fails, cancel all remaining tasks and rethrow the exception
                            _logger.LogError(ex, "An error occurred during embedding generation. Cancelling all remaining tasks.");
                            cancellationTokenSource.Cancel();
                            throw new InvalidOperationException("Failed to generate embeddings. Cancelling all remaining tasks.", ex);
                        }
                    }
                    
                    // If we get here, check if cancellation was requested
                    if (cancellationTokenSource.IsCancellationRequested)
                    {
                        _logger.LogWarning("Embedding generation was cancelled before completion");
                        throw new InvalidOperationException("Embedding generation was cancelled due to errors in one or more chunks.");
                    }
                }
                catch (AggregateException ae)
                {
                    // Extract all the inner exceptions from the AggregateException
                    var exceptions = ae.Flatten().InnerExceptions;
                    _logger.LogError("Multiple errors occurred during embedding generation. First error: {FirstError}", exceptions[0].Message);
                    throw new InvalidOperationException("Failed to generate embeddings for one or more chunks. See inner exception for details.", ae.Flatten());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred during embedding generation");
                    throw new InvalidOperationException("Failed to generate embeddings. See inner exception for details.", ex);
                }
                finally
                {
                    // Dispose the cancellation token source
                    cancellationTokenSource.Dispose();
                }
                _logger.LogInformation("Completed embedding generation for all {Count} chunks", totalChunks);

                // If requested, save a copy to blob storage
                if (saveToBlobStorage)
                {
                    string blobPath = $"{collectionName}/{fileName}";
                    await SaveFileToBlobStorageAsync(filePath, blobPath);
                    resultsDictionary["blob_path"] = blobPath;
                }

                // Upsert the chunk records into the collection
                _logger.LogInformation("Upserting chunk records into {CollectionName}...", collectionName);
                // var options = new UpsertRecordOptions();
                // var upsertTasks = chunkingResult.chunkRecords.Select(r => (Task)collection.UpsertAsync(r, options, cancellationToken));
                // await Task.WhenAll(upsertTasks);
                // Process in batches of 10 (or another appropriate number)
                int batchSize = 10;
                for (int i = 0; i < chunkingResult.chunkRecords.Count; i += batchSize)
                {
                    var batch = chunkingResult.chunkRecords.Skip(i).Take(batchSize);
                    // var upsertTasks = batch.Select(r => collection.UpsertAsync(r, cancellationToken));
                    // await Task.WhenAll<Task<object>>(upsertTasks);
                    var upsertTasks = batch.Select(r => collection.UpsertAsync(r, cancellationToken));
                    await Task.WhenAll(upsertTasks.Select(t => (Task)t));
                }

                // Clean up temporary file if needed
                if (deleteTempFile && File.Exists(filePath))
                {
                    try
                    {
                        File.Delete(filePath);
                        _logger.LogInformation("Deleted temporary file: {TempFilePath}", filePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete temporary file: {TempFilePath}", filePath);
                    }
                }

                _logger.LogInformation("Successfully ingested {Count} chunks from file '{fileName}' into collection '{CollectionName}'.",
                    chunkingResult.chunkRecords.Count, fileName, collectionName);
                
                resultsDictionary["success"] = true;
                resultsDictionary["message"] = $"Successfully ingested {chunkingResult.chunkRecords.Count} chunks from file {fileName} into collection {collectionName}.";
                resultsDictionary["chunk_count"] = chunkingResult.chunkRecords.Count;
                
                _logger.LogInformation("Embedder complete [4/4 complete]");
                return resultsDictionary;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing file.");
                resultsDictionary["success"] = false;
                resultsDictionary["message"] = $"ProcessFileAsync error: {ex.Message}";
                return resultsDictionary;
            }
        }

        /// <summary>
        /// Ingest an uploaded file into a collection.
        /// </summary>
        /// <param name="collectionName">Name of the collection to ingest into</param>
        /// <param name="chunkSize">Size of chunks in words</param>
        /// <param name="chunkOverlapFraction">Fraction of chunk size to overlap</param>
        /// <param name="file">File to ingest</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary with operation results</returns>
        public override async Task<Dictionary<string, object>> IngestUploadedFileAsync(string collectionName,
                                                                                        int chunkSize,
                                                                                        float chunkOverlapFraction,
                                                                                        IFormFile file,
                                                                                        bool saveToBlobStorage = false,
                                                                                        CancellationToken cancellationToken = default)
        {
            var resultsDictionary = new Dictionary<string, object>
            {
                { "success", (bool?)null },
                { "message", (string?)null },
                { "file_source", "upload" }
            };

            try
            {
                // Create a temporary file from the uploaded content
                string tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + Path.GetExtension(file.FileName));
                // string fileName = file.FileName;
                
                _logger.LogInformation("Creating temporary file for uploaded content: {TempFilePath}", tempFilePath);
                
                // Save the uploaded file to the temp location
                using (var stream = new FileStream(tempFilePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream, cancellationToken);
                }
                // Save the uploaded file to the root directory as test.pdf
                string rootFilePath = Path.Combine(Directory.GetCurrentDirectory(), "test.pdf");
                _logger.LogInformation("Saving uploaded file to root directory as: {RootFilePath}", rootFilePath);

                using (var rootStream = new FileStream(rootFilePath, FileMode.Create))
                {
                    await file.CopyToAsync(rootStream, cancellationToken);
                }
                
                // // If requested, save a copy to blob storage
                // if (saveToBlobStorage)
                // {
                //     string blobPath = $"{collectionName}/{file.FileName}";
                //     await SaveFileToBlobStorageAsync(tempFilePath, blobPath);
                //     resultsDictionary["blob_path"] = blobPath;
                // }
                
                // Process the file using the common method
                var processingResult = await ProcessFileAsync(
                    collectionName,
                    chunkSize,
                    chunkOverlapFraction,
                    tempFilePath,
                    file.FileName,
                    true, // This is a temp file that should be deleted after processing
                    saveToBlobStorage,
                    cancellationToken);
                
                // Merge the processing results with our results
                foreach (var kvp in processingResult)
                {
                    resultsDictionary[kvp.Key] = kvp.Value;
                }
                
                return resultsDictionary;
            }
            catch (Exception ex)
            {
                _typedLogger.LogError(ex, "Error ingesting uploaded image file into collection {CollectionName}", collectionName);
                throw new InvalidOperationException($"Error ingesting image file: {ex.Message}", ex);
            }
        }

        /// <param name="collectionName">Name of the collection to ingest into</param>
        /// <param name="chunkSize">Size of chunks in words</param>
        /// <param name="chunkOverlapFraction">Fraction of chunk size to overlap</param>
        /// <param name="filePath">Path to the file on the server</param>
        /// <param name="saveToBlobStorage">Whether to save a copy to blob storage</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary with operation results</returns>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        public override async Task<Dictionary<string, object>> IngestLocalFileAsync(
            string collectionName,
            int chunkSize,
            float chunkOverlapFraction,
            string filePath,
            bool saveToBlobStorage = false,
            CancellationToken cancellationToken = default)
        {
            var resultsDictionary = new Dictionary<string, object>
            {
                { "success", (bool?)null },
                { "message", (string?)null },
                { "file_source", "local" }
            };

            try
            {
                // Validate local file exists
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"Local file not found: {filePath}");
                }
                
                string fileName = Path.GetFileName(filePath);
                
                // // If requested, save a copy to blob storage
                // if (saveToBlobStorage)
                // {
                //     string blobRelativePath = Path.Join(collectionName, filePath);
                //     // string blobPath = $"{collectionName}/{fileName}";
                //     await SaveFileToBlobStorageAsync(filePath, blobRelativePath);
                //     resultsDictionary["blob_path"] = blobRelativePath;
                // }
                
                // Process the file using the common method
                var processingResult = await ProcessFileAsync(
                    collectionName,
                    chunkSize,
                    chunkOverlapFraction,
                    filePath,
                    fileName,
                    false, // This is not a temp file, don't delete it
                    saveToBlobStorage,
                    cancellationToken);
                
                // Merge the processing results with our results
                foreach (var kvp in processingResult)
                {
                    resultsDictionary[kvp.Key] = kvp.Value;
                }
                
                return resultsDictionary;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while ingesting a local file into the collection.");
                resultsDictionary["success"] = false;
                resultsDictionary["message"] = $"IngestLocalFileAsync error: {ex.Message}";
                return resultsDictionary;
            }
        }



        /// <summary>
        /// Ingest text content into a collection.
        /// </summary>
        /// <param name="collectionName">Name of the collection to ingest into</param>
        /// <param name="chunkSize">Size of chunks in words</param>
        /// <param name="chunkOverlapFraction">Fraction of chunk size to overlap</param>
        /// <param name="content">The text content to ingest</param>
        /// <param name="saveToBlobStorage">Whether to save a copy to blob storage</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary with operation results</returns>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        public override async Task<Dictionary<string, object>> NewTextMemoryAsync(
            string collectionName,
            int chunkSize,
            float chunkOverlapFraction,
            string content,
            bool saveToBlobStorage = false,
            CancellationToken cancellationToken = default)
        {
            var resultsDictionary = new Dictionary<string, object>
            {
                { "success", (bool?)null },
                { "message", (string?)null },
                { "content_source", "direct_text" }
            };

            try
            {
                // Create a temporary file to store the content
                string tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".txt");
                string fileName = $"text_memory_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt";
                
                _logger.LogInformation("Creating temporary file for text content: {TempFilePath}", tempFilePath);
                
                // Save the content to the temp file
                await File.WriteAllTextAsync(tempFilePath, content, cancellationToken);
                
                // // If requested, save a copy to blob storage
                // if (saveToBlobStorage)
                // {
                //     string blobPath = $"{collectionName}/{fileName}";
                //     await SaveFileToBlobStorageAsync(tempFilePath, blobPath);
                //     resultsDictionary["blob_path"] = blobPath;
                // }
                
                // Process the file using the common method
                var processingResult = await ProcessFileAsync(
                    collectionName,
                    chunkSize,
                    chunkOverlapFraction,
                    tempFilePath,
                    fileName,
                    true, // This is a temp file that should be deleted after processing
                    saveToBlobStorage,
                    cancellationToken);
                
                // Merge the processing results with our results
                foreach (var kvp in processingResult)
                {
                    resultsDictionary[kvp.Key] = kvp.Value;
                }
                
                return resultsDictionary;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while ingesting text content into the collection.");
                resultsDictionary["success"] = false;
                resultsDictionary["message"] = $"IngestTextAsync error: {ex.Message}";
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
            // Create a place holder of the responseDict
            // var resultsDictionary = new Dictionary<string, object>
            // {
            //     { "success", (bool?)null },
            //     { "message", (string?)null },
            //     { "results", (Dictionary<int, Dictionary<object, object>>?)null }
            // };
            List<EmbeddingSearchResult> results;

            try
            {
                List<EmbeddingSearchResult> embeddingsList;
                // Check to make sure that the collection exists
                bool collectionExists = await _schemaManagementService.CheckCollectionExists(collectionName);
                if (!collectionExists)
                {
                    throw new InvalidOperationException($"Collection named {collectionName} does not exists.");
                }
                else
                {
                    string embeddingServiceId;
                    string embeddingServiceModality;
                    (embeddingServiceId, embeddingServiceModality) = await _schemaManagementService.getDefaultEmbeddingServiceDetails(collectionName);
                    string validatedEmbeddingModelName = await _schemaManagementService.getEmbeddingServiceName(embeddingServiceId);
                    _logger.LogInformation($"Embedding model {validatedEmbeddingModelName} is valid for collection {collectionName}");
                    
                    // Calculate the time window cutoff date if time-window filtering is enabled
                    DateTime? cutoffDate = null;
                    // if (timeWindowEnabled)
                    // {
                    //     cutoffDate = DateTime.UtcNow.AddDays(-windowDays);
                    //     _logger.LogInformation($"Time-window filtering enabled. Only including results after {cutoffDate}.");
                    // }
                    
                    results = await VectorSearchAsync(collectionName,
                                                                    validatedEmbeddingModelName,
                                                                    embeddingServiceId,
                                                                    query,
                                                                    nResults,
                                                                    timeWindowEnabled,
                                                                    windowDays,
                                                                    temporalDecayEnabled,
                                                                    decayImpact
                                                                    );
                }
                // resultsDictionary["results"] = embeddingsList;
                // resultsDictionary["success"] = true;
                // resultsDictionary["message"] = $"Succssfully ran SearchFixedCollectionAsync, see 'results' field for nearest neighbors";

                if (temporalDecayEnabled)
                {
                    DateTime referenceTimestamp = DateTime.UtcNow;
                    _logger.LogInformation($"Temporal decay enabled. Using {referenceTimestamp} as the reference timestamp.");
                    // Apply exponential decay to final RRF scores
                    foreach (var result in results)
                    {
                        double decayMultiplier = ApplyExponentialDecay(result.Timestamp, referenceTimestamp, decayImpact);
                        result.Score *= decayMultiplier;
                        _logger.LogInformation($"Score for result {result.FileName}_{result.ChunkIndex} after decay: {result.Score} (multiplier={decayMultiplier})");
                    }
                    results = results
                        .OrderByDescending(r => r.Score)
                        .ToList();
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while searching the fixed collection.");
                // resultsDictionary["success"] = false;
                // resultsDictionary["message"] = $"SearchFixedCollectionAsync error: {ex.Message}";
                return new List<EmbeddingSearchResult>();
            }
        }

        /// Original implementation without keyword filtering
        /// <summary>
        /// BM25 semantic search async method
        /// </summary>
        /// <param name="collectionName"></param>
        /// <param name="query"></param>
        /// <param name="nResults"></param>
        /// <param name="cutoffDate"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        // public async Task<List<EmbeddingSearchResult>> BM25SearchAsync(
        //                                                     string collectionName,
        //                                                     string query,
        //                                                     int? nResults = null,
        //                                                     DateTime? cutoffDate = null,
        //                                                     CancellationToken cancellationToken = default
        //                                                 )
        // {
        //     var top = nResults ?? 10;

        //     // Parse the query into terms
        //     var terms = query.ToLower()
        //     .Split(new[] { ' ', ',', '.', '!', '?', ';', ':', '-', '(', ')', '[', ']', '{', '}' }, 
        //         StringSplitOptions.RemoveEmptyEntries);

        //     // BM25 parameters
        //     double k1 = 1.2;  // Term frequency saturation
        //     double b = 0.75;  // Length normalization factor

        //     /*
        //         document_stats - average word count of all definitions after splitting on whitespace
        //         term_stats - 
        //     */
        //     // Create a SQL query with proper parameter handling
        //     string timestampFilter = cutoffDate.HasValue
        //         ? $"WHERE \"Timestamp\" >= @{nameof(cutoffDate)}"
        //         : "";

        //     var sql = @$"
        //         WITH document_stats AS (
        //             SELECT 
        //                 AVG(ARRAY_LENGTH(regexp_split_to_array(TRIM(""Definition"") ||''||TRIM(""FileName""), '\s+'), 1))::double precision AS avg_length
        //             FROM ""{collectionName}""
        //             {timestampFilter}
        //         ),
        //         term_stats AS (
        //             SELECT 
        //                 clean_term AS term,
        //                 COUNT(*)::double precision AS doc_count
        //             FROM (
        //                 SELECT 
        //                     ""Key"",
        //                     regexp_replace(term, '[^\w]', '', 'g') AS clean_term
        //                 FROM (
        //                     SELECT 
        //                         ""Key"",
        //                         unnest(regexp_split_to_array(lower(""Definition"" ||''|| ""FileName""), '\s+')) AS term
        //                     FROM ""{collectionName}""
        //                     {timestampFilter}
        //                 ) AS raw_terms
        //                 WHERE length(regexp_replace(term, '[^\w]', '', 'g')) > 0
        //             ) AS terms
        //             WHERE clean_term = ANY(@terms)
        //             GROUP BY clean_term
        //         ),
        //         bm25_scores AS (
        //             SELECT 
        //                 d.""Key"",
        //                 d.""FileName"",
        //                 d.""ChunkIndex"",
        //                 d.""Definition"",
        //                 d.""EmbeddingServiceId"",
        //                 d.""Timestamp"",
        //                 NULLIF(
        //                     SUM(
        //                         (SELECT COUNT(*)::double precision FROM regexp_matches(lower(d.""Definition"" || ' ' || d.""FileName""), concat('\y', t.term, '\y'), 'g')) *
        //                         LN(
        //                                 GREATEST(0.000001::double precision, 
        //                                     ((SELECT COUNT(*)::double precision FROM ""{collectionName}"" {timestampFilter}) - t.doc_count + 0.5) / 
        //                                     (t.doc_count + 0.5)
        //                                 )
        //                             ) *
        //                         (({k1}::double precision + 1) / GREATEST(0.000001::double precision, {k1}::double precision * (1 - {b}::double precision + {b}::double precision * (LENGTH(d.""Definition"" || ' ' || d.""FileName"")::double precision / GREATEST(1::double precision, (SELECT avg_length FROM document_stats)))))) + 
        //                         GREATEST(1::double precision, (SELECT COUNT(*)::double precision FROM regexp_matches(lower(d.""Definition"" || ' ' || d.""FileName""), concat('\y', t.term, '\y'), 'g')))
        //                     )::double precision, 'Infinity'::double precision
        //                 ) AS bm25_score
        //             FROM 
        //                 ""{collectionName}"" d
        //             JOIN term_stats t ON lower(d.""Definition"" || ' ' || d.""FileName"") SIMILAR TO '%(^|[^a-z])' || t.term || '([^a-z]|$)%'
        //             {timestampFilter.Replace("WHERE", "WHERE d.")}
        //             GROUP BY 
        //                 d.""Key"", d.""FileName"", d.""ChunkIndex"", d.""Definition"", d.""EmbeddingServiceId"", d.""Timestamp""
        //         )
        //         SELECT 
        //             bs.*,
        //             COALESCE(bs.bm25_score, (SELECT MAX(bm25_score) FROM bm25_scores WHERE bm25_score IS NOT NULL)) AS score
        //         FROM 
        //             bm25_scores bs
        //         ORDER BY 
        //             bm25_score DESC
        //         LIMIT {top};";


        //     using (var connection = _connectionFactory.CreateConnection())
        //     {
        //         await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        //         using (var command = new NpgsqlCommand(sql, connection))
        //         {
        //             command.Parameters.AddWithValue("@terms", terms);

        //             if (cutoffDate.HasValue)
        //             {
        //                 command.Parameters.AddWithValue("@cutoffDate", cutoffDate.Value);
        //             }
        //             command.CommandTimeout = 300; // 5 minutes
        //             await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        //             {
        //                 var results = new List<EmbeddingSearchResult>();
        //                 var i = 1;

        //                 while (await reader.ReadAsync(cancellationToken))
        //                 {
        //                     var score = reader.GetDouble(reader.GetOrdinal("score"));
        //                     var fileName = reader.GetString(reader.GetOrdinal("FileName"));
        //                     var chunkIndex = reader.GetInt32(reader.GetOrdinal("ChunkIndex"));
        //                     var definition = reader.GetString(reader.GetOrdinal("Definition"));
        //                     var embeddingServiceId = reader.GetString(reader.GetOrdinal("EmbeddingServiceId"));
        //                     var timestamp = reader.GetDateTime(reader.GetOrdinal("Timestamp"));

        //                     // Get embedding model name
        //                     string embeddingModelName = await _schemaManagementService.getEmbeddingServiceName(embeddingServiceId);

        //                     // _logger.LogInformation("BM25 Result {Index} Score: {Score}", i, score);
        //                     // _logger.LogInformation("BM25 Result {Index}: {Definition}", i, definition);

        //                     EmbeddingSearchResult result = new EmbeddingSearchResult(
        //                         i,
        //                         score,
        //                         fileName,
        //                         chunkIndex,
        //                         definition,
        //                         embeddingModelName,
        //                         timestamp
        //                     );

        //                     results.Add(result);
        //                     i++;
        //                 }

        //                 return results;
        //             }
        //         }
        //     }
        // }

        /// <summary>
        /// BM25 semantic search async method
        /// </summary>
        /// <param name="collectionName"></param>
        /// <param name="query"></param>
        /// <param name="nResults"></param>
        /// <param name="timeWindowEnabled"></param>
        /// <param name="windowDays"></param>
        /// <param name="temporalDecayEnabled"></param>
        /// <param name="decayImpact"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<List<EmbeddingSearchResult>> BM25SearchAsync(
            string collectionName,
            string query,
            int? nResults = null,
            bool? timeWindowEnabled = false,
            int? windowDays = 30,
            bool? temporalDecayEnabled = false,
            double? decayImpact = 0.01,
            CancellationToken cancellationToken = default)
        {
            SqlIdentifierValidator.Validate(collectionName, nameof(collectionName));
            var top = nResults ?? 10;

            // Parse the query into terms
            var terms = query.ToLower()
                .Split(new[] { ' ', ',', '.', '!', '?', ';', ':', '-', '(', ')', '[', ']', '{', '}' },
                    StringSplitOptions.RemoveEmptyEntries);

            // BM25 parameters
            double k1 = 1.2;  // Term frequency saturation
            double b = 0.75;  // Length normalization factor

            // string timestampFilter = cutoffDate.HasValue
            //     ? $"WHERE \"Timestamp\" >= @{nameof(cutoffDate)}"
            //     : "";

            // // Pre-filter using keyword presence
            // var keywordFilterSql = $"SELECT \"Key\" FROM \"{collectionName}\" {timestampFilter} AND (\"Definition\" || ' ' || \"FileName\") ILIKE ANY (@keywordPatterns)";

            var keywordPatterns = terms.Select(t => $"%{t}%").ToArray();
            int paramIndex = 1;
            var conditions = new List<string>();
            var parameters = new List<NpgsqlParameter>();
            var parametersDuplicate = new List<NpgsqlParameter>();

            // if (cutoffDate.HasValue)
            // {
            //     conditions.Add($@"""Timestamp"" >= ${paramIndex}");
            //     parameters.Add(new NpgsqlParameter { Value = cutoffDate.Value });
            //     paramIndex++;
            // }

            // conditions.Add($@"(""Definition"" || ' ' || ""FileName"") ILIKE ANY (${paramIndex}::text[])");
            // parameters.Add(new NpgsqlParameter
            // {
            //     Value = keywordPatterns,
            //     NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text
            // });

            // string whereClause = "WHERE " + string.Join(" AND ", conditions);

            var bm25Conditions = "";
            DateTime? cutoffDate = null;
            if (timeWindowEnabled.HasValue && timeWindowEnabled.Value == true && windowDays.HasValue)
            {
                // Calculate the cutoff date based on the number of days
                cutoffDate = DateTime.UtcNow.AddDays(-windowDays.Value);
                _logger.LogInformation($"Time-window filtering enabled. Only including results after {cutoffDate}.");
                conditions.Add($@"""Timestamp"" >= ${paramIndex}");
                // parameters.Add(new NpgsqlParameter($"@p{paramIndex}", NpgsqlTypes.NpgsqlDbType.Timestamp) { Value = cutoffDate.Value });
                parameters.Add(new NpgsqlParameter($"@p{paramIndex}", NpgsqlTypes.NpgsqlDbType.TimestampTz)
                {
                    Value = cutoffDate
                });
                parametersDuplicate.Add(new NpgsqlParameter($"@p{paramIndex}", NpgsqlTypes.NpgsqlDbType.TimestampTz)
                {
                    Value = cutoffDate
                });
                paramIndex++;
                bm25Conditions = $"WHERE d.\"Timestamp\" >= @cutoffDate";
            }

            conditions.Add($@"(""Definition"" || ' ' || ""FileName"") ILIKE ANY(${paramIndex}::text[])");
            parameters.Add(new NpgsqlParameter($"@p{paramIndex}", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text)
            {
                Value = keywordPatterns
            });
            parametersDuplicate.Add(new NpgsqlParameter($"@p{paramIndex}", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text)
            {
                Value = keywordPatterns
            });

            // Now fix the `whereClause` to use named parameters explicitly
            string whereClause = "WHERE " + string.Join(" AND ", conditions.Select((_, idx) => _.Replace($"${idx + 1}", $"@p{idx + 1}")));

            var keywordFilterSql = $@"SELECT ""Key"" FROM ""{collectionName}"" {whereClause}";

            var filteredKeys = new List<string>();

            using (var connection = _connectionFactory.CreateConnection())
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                using (var keywordCommand = new NpgsqlCommand(keywordFilterSql, connection))
                {
                    keywordCommand.Parameters.AddRange(parameters.ToArray());

                    using (var reader = await keywordCommand.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                            filteredKeys.Add(reader.GetGuid(0).ToString());
                    }
                }

                if (!filteredKeys.Any())
                    return new List<EmbeddingSearchResult>();

                // var filteredKeysParam = string.Join(",", filteredKeys.Select(k => $"'{k}'"));
                var filteredKeysArray = filteredKeys.Select(Guid.Parse).ToArray();

                // var bm25Conditions = cutoffDate
                //     ? $"WHERE d.\"Timestamp\" >= @cutoffDate"
                //     : "";

                var bm25Sql = @$"
                WITH document_stats AS (
                    SELECT 
                        AVG(ARRAY_LENGTH(regexp_split_to_array(TRIM(""Definition"") ||''||TRIM(""FileName""), '\s+'), 1))::double precision AS avg_length
                    FROM ""{collectionName}""
                    {whereClause}
                ),
                term_stats AS (
                    SELECT 
                        clean_term AS term,
                        COUNT(*)::double precision AS doc_count
                    FROM (
                        SELECT 
                            ""Key"",
                            regexp_replace(term, '[^\w]', '', 'g') AS clean_term
                        FROM (
                            SELECT 
                                ""Key"",
                                unnest(regexp_split_to_array(lower(""Definition"" ||''|| ""FileName""), '\s+')) AS term
                            FROM ""{collectionName}""
                            {whereClause}
                        ) AS raw_terms
                        WHERE length(regexp_replace(term, '[^\w]', '', 'g')) > 0
                    ) AS terms
                    WHERE clean_term = ANY(@terms)
                    GROUP BY clean_term
                ),
                bm25_scores AS (
                    SELECT 
                        d.*,
                        NULLIF(
                            SUM(
                                (SELECT COUNT(*)::double precision FROM regexp_matches(lower(d.""Definition"" || ' ' || d.""FileName""), concat('\y', t.term, '\y'), 'g')) *
                                LN(
                                        GREATEST(0.000001::double precision, 
                                            ((SELECT COUNT(*)::double precision FROM ""{collectionName}"" {whereClause}) - t.doc_count + 0.5) / 
                                            (t.doc_count + 0.5)
                                        )
                                    ) *
                                (({k1}::double precision + 1) / GREATEST(0.000001::double precision, {k1}::double precision * (1 - {b}::double precision + {b}::double precision * (LENGTH(d.""Definition"" || ' ' || d.""FileName"")::double precision / GREATEST(1::double precision, (SELECT avg_length FROM document_stats)))))) + 
                                GREATEST(1::double precision, (SELECT COUNT(*)::double precision FROM regexp_matches(lower(d.""Definition"" || ' ' || d.""FileName""), concat('\y', t.term, '\y'), 'g')))
                            )::double precision, 'Infinity'::double precision
                        ) AS bm25_score
                    FROM 
                        ""{collectionName}"" d
                    JOIN term_stats t ON lower(d.""Definition"" || ' ' || d.""FileName"") SIMILAR TO '%(^|[^a-z])' || t.term || '([^a-z]|$)%'
                    {bm25Conditions}
                    GROUP BY 
                        d.""Key"", d.""FileName"", d.""ChunkIndex"", d.""Definition"", d.""EmbeddingServiceId"", d.""Timestamp""
                )
                SELECT 
                    bs.*,
                    COALESCE(bs.bm25_score, (SELECT MAX(bm25_score) FROM bm25_scores WHERE bm25_score IS NOT NULL)) AS score
                FROM 
                    bm25_scores bs
                WHERE bs.""Key"" = ANY(@filteredKeys)
                ORDER BY score DESC
                LIMIT {top};";

                using (var bm25Command = new NpgsqlCommand(bm25Sql, connection))
                {
                    bm25Command.Parameters.AddWithValue("@terms", terms);
                    bm25Command.Parameters.AddWithValue("@filteredKeys", filteredKeysArray);
                    bm25Command.Parameters.AddWithValue("@top", top);
                    bm25Command.Parameters.AddRange(parametersDuplicate.ToArray());

                    if (timeWindowEnabled.HasValue && timeWindowEnabled.Value == true && windowDays.HasValue && cutoffDate.HasValue)
                        bm25Command.Parameters.AddWithValue("@cutoffDate", cutoffDate.Value);

                    bm25Command.CommandTimeout = 300;

                    var results = new List<EmbeddingSearchResult>();

                    await using (var reader = await bm25Command.ExecuteReaderAsync(cancellationToken))
                    {
                        var i = 1;
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            var score = reader.GetDouble(reader.GetOrdinal("score"));
                            var fileName = reader.GetString(reader.GetOrdinal("FileName"));
                            var chunkIndex = reader.GetInt32(reader.GetOrdinal("ChunkIndex"));
                            var definition = reader.GetString(reader.GetOrdinal("Definition"));
                            var embeddingServiceId = reader.GetString(reader.GetOrdinal("EmbeddingServiceId"));
                            var timestamp = reader.GetDateTime(reader.GetOrdinal("Timestamp"));

                            string embeddingModelName = await _schemaManagementService.getEmbeddingServiceName(embeddingServiceId);

                            EmbeddingSearchResult result = new EmbeddingSearchResult(
                                i++, score, fileName, chunkIndex, definition, embeddingModelName, timestamp);

                            results.Add(result);
                        }
                    }
                    if (temporalDecayEnabled.HasValue && temporalDecayEnabled.Value)
                    {
                        DateTime referenceTimestamp = DateTime.UtcNow;
                        _logger.LogInformation($"Temporal decay enabled. Using {referenceTimestamp} as the reference timestamp.");
                        // Apply exponential decay to final BM25 scores
                        foreach (var result in results)
                        {
                            double decayMultiplier = ApplyExponentialDecay(result.Timestamp, referenceTimestamp, decayImpact ?? 0.01);
                            result.Score *= decayMultiplier;
                            _logger.LogInformation($"Score for result {result.FileName}_{result.ChunkIndex} after decay: {result.Score} (multiplier={decayMultiplier})");
                        }
                        results = results
                            .OrderByDescending(r => r.Score)
                            .ToList();
                    }
                    return results;
                }
            }
        }

        /// <summary>
        /// Searches a vector collection for the most similar embeddings to the given query.
        /// </summary>
        /// <param name="collectionName"></param>
        /// <param name="embeddingModelName"></param>
        /// <param name="embeddingServiceId"></param>
        /// <param name="query"></param>
        /// <param name="nResults"></param>
        /// <param name="timeWindowEnabled"></param>
        /// <param name="windowDays"></param>
        /// <param name="temporalDecayEnabled"></param>
        /// <param name="decayImpact"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<List<EmbeddingSearchResult>> VectorSearchAsync(string collectionName,
                                                                        string embeddingModelName,
                                                                        string embeddingServiceId,
                                                                        string query,
                                                                        int? nResults = null,
                                                                        bool? timeWindowEnabled = false,
                                                                        int? windowDays = 30,
                                                                        bool? temporalDecayEnabled = false,
                                                                        double? decayImpact = 0.01,
                                                                        CancellationToken cancellationToken = default)
        {
            SqlIdentifierValidator.Validate(collectionName, nameof(collectionName));
            var top = nResults ?? 10;

            IEmbeddingGenerator<string, Embedding<float>> textEmbeddingService;
            try
            {
                textEmbeddingService = _kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(embeddingServiceId.ToString());
            }
            catch
            {
                var legacyService = _kernel.GetRequiredService<ITextEmbeddingGenerationService>(embeddingServiceId.ToString());
                textEmbeddingService = legacyService.AsEmbeddingGenerator();
            }

            // var searchVector = await textEmbeddingService.GenerateEmbeddingAsync(query);
            var embeddingResult = await textEmbeddingService.GenerateAsync(query);
            var searchVector = embeddingResult.Vector.ToArray();
            // var embeddingValues = new float[searchVector.Length];
            // searchVector.CopyTo(embeddingValues);

            // Build cutoff date if required by the time window logic
            DateTime? cutoffDate = null;
            if (timeWindowEnabled == true && windowDays.HasValue)
            {
                cutoffDate = DateTime.UtcNow.AddDays(-windowDays.Value);
                _logger.LogInformation($"Time-window filtering enabled. Only including results after {cutoffDate}.");
            }

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
                    // Embedding paramter
                    var embedding = new Pgvector.Vector(searchVector);
                    command.Parameters.AddWithValue("@embedding", embedding);
                    command.Parameters["@embedding"].DataTypeName = "vector";
                    command.Parameters.AddWithValue("@embeddingServiceId", embeddingServiceId);

                    // Only add cutoff date parameter if filter is active
                    if (cutoffDate != null)
                    {
                        command.Parameters.Add(new NpgsqlParameter("@cutoffDate", NpgsqlTypes.NpgsqlDbType.TimestampTz)
                        {
                            Value = cutoffDate.Value
                        });
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
                                DefinitionEmbedding = reader.GetFieldValue<Vector>(reader.GetOrdinal("DefinitionEmbedding")),
                                Timestamp = reader.IsDBNull(reader.GetOrdinal("Timestamp")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("Timestamp"))
                            };    // Assuming EmbeddingSearchResult has a constructor that takes a data reader
                            // embeddingsList.Add(new EmbeddingSearchResult(reader));

                            double cosineDistance = CalculateCosineDistance(record.DefinitionEmbedding.ToArray(), searchVector.ToArray());
                            // var cosineDistance = 0;

                            // _logger.LogInformation("Result {Index} Score: {Score}", i, cosineDistance);
                            // _logger.LogInformation("Result {Index}: {Definition}", i, record.Definition);

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
                        // Optional: Apply temporal decay if enabled
                        if (temporalDecayEnabled == true)
                        {
                            DateTime referenceTimestamp = DateTime.UtcNow;
                            _logger.LogInformation($"Temporal decay enabled. Using {referenceTimestamp} as the reference timestamp.");
                            foreach (var result in embeddingsList)
                            {
                                double decayMultiplier = ApplyExponentialDecay(result.Timestamp, referenceTimestamp, decayImpact ?? 0.01);
                                result.Score *= decayMultiplier;
                                _logger.LogInformation($"Score for result {result.FileName}_{result.ChunkIndex} after decay: {result.Score} (multiplier={decayMultiplier})");
                            }
                            embeddingsList = embeddingsList.OrderByDescending(r => r.Score).ToList();
                        }

                        return embeddingsList;
                    }
                }
            }
        }

        
        /// <summary>
        /// RRF ranking method implementation for hybrid search
        /// </summary>
        /// <param name="vectorResults"></param>
        /// <param name="bm25Results"></param>
        /// <param name="alpha"></param>
        /// <param name="k"></param>
        /// <returns></returns>
        private List<EmbeddingSearchResult> ApplyRRF(
            List<EmbeddingSearchResult> vectorResults, 
            List<EmbeddingSearchResult> bm25Results, 
            double alpha = 0.5,
            int k = 50)
        {
            // Create a dictionary to store combined scores
            var combinedScores = new Dictionary<string, (EmbeddingSearchResult Result, double Score)>();
            
            // Process vector search results
            for (int i = 0; i < vectorResults.Count; i++)
            {
                var result = vectorResults[i];
                var key = $"{result.FileName}_{result.ChunkIndex}";
                var rank = i + 1;
                var score = alpha * (1.0 / (rank + k));
                
                combinedScores[key] = (result, score);
            }
            
            // Process BM25 search results
            for (int i = 0; i < bm25Results.Count; i++)
            {
                var result = bm25Results[i];
                var key = $"{result.FileName}_{result.ChunkIndex}";
                var rank = i + 1;
                var score = (1 - alpha) * (1.0 / (rank + k));
                
                if (combinedScores.TryGetValue(key, out var existing))
                {
                    combinedScores[key] = (existing.Result, existing.Score + score);
                }
                else
                {
                    combinedScores[key] = (result, score);
                }
            }
            
            // Sort by combined score and return
            return combinedScores.Values
                .OrderByDescending(x => x.Score)
                .Select((x, i) => new EmbeddingSearchResult(
                    i + 1,
                    x.Score,
                    x.Result.FileName,
                    x.Result.ChunkIndex,
                    x.Result.Definition,
                    x.Result.EmbeddingModelName,
                    x.Result.Timestamp
                ))
                .ToList();
        }

        /// <summary>
        /// Core method for implementing hybrid search with optional time-window filtering.
        /// </summary>
        /// <param name="query">The search query</param>
        /// <param name="collectionName">Name of the collection to search</param>
        /// <param name="nResults">Number of results to return</param>
        /// <param name="filterByCategory">Optional category filter</param>
        /// <param name="alpha">Weight factor for hybrid search (0.5 is balanced)</param>
        /// <param name="timeWindowEnabled">Whether to enable time-window filtering</param>
        /// <param name="windowDays">Number of days to include in the time window</param>
        /// <param name="temporalDecayEnabled">Whether to apply exponential decay based on timestamp</param>
        /// <param name="decayImpact">Decay impact factor for exponential decay. 0.005 - low, 0.01 - medium, 0.05 - high</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of search results</returns>
        /// <exception cref="InvalidOperationException">Thrown when collection doesn't exist</exception>
        public async Task<List<EmbeddingSearchResult>> HybridSearchAsync(
            string query,
            string collectionName,
            int? nResults = null,
            double alpha = 0.5,
            bool timeWindowEnabled = false,
            int windowDays = 30,
            bool temporalDecayEnabled = false,
            double decayImpact = 0.01,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Check to make sure that the collection exists
                bool collectionExists = await _schemaManagementService.CheckCollectionExists(collectionName);
                if (!collectionExists)
                {
                    throw new InvalidOperationException($"Collection named {collectionName} does not exist.");
                }

                // Get embedding service information
                string embeddingServiceId;
                string embeddingServiceModality;
                string embeddingServiceName;
                (embeddingServiceId, embeddingServiceModality) = await _schemaManagementService.getDefaultEmbeddingServiceDetails(collectionName);
                embeddingServiceName = await _schemaManagementService.getEmbeddingServiceName(embeddingServiceId);
                _logger.LogInformation($"Embedding model {embeddingServiceId} is valid for collection {collectionName}");

                // Increase the number of results for individual searches to ensure we have enough candidates
                int searchResultCount = (nResults ?? 10) * 3;

                // Calculate the time window cutoff date if time-window filtering is enabled
                // DateTime? cutoffDate = null;
                // if (timeWindowEnabled)
                // {
                //     cutoffDate = DateTime.UtcNow.AddDays(-windowDays);
                //     _logger.LogInformation($"Time-window filtering enabled. Only including results after {cutoffDate}.");
                // }

                // Run both searches in parallel
                var vectorSearchTask = VectorSearchAsync(
                    collectionName,
                    embeddingServiceName,
                    embeddingServiceId,
                    query,
                    searchResultCount,
                    timeWindowEnabled,
                    windowDays,
                    false, // temporalDecayEnabled is handled separately
                    0.0, // decayImpact is not used for vector search
                    cancellationToken
                );

                var bm25SearchTask = BM25SearchAsync(
                    collectionName,
                    query,
                    searchResultCount,
                    timeWindowEnabled,
                    windowDays,
                    false, // temporalDecayEnabled is handled separately
                    0.0, // decayImpact is not used for BM25 search
                    cancellationToken
                );

                // Wait for both searches to complete
                await Task.WhenAll(vectorSearchTask, bm25SearchTask);

                var vectorResults = await vectorSearchTask;
                var bm25Results = await bm25SearchTask;

                // Combine results using RRF
                var combinedResults = ApplyRRF(vectorResults, bm25Results, alpha);

                if (temporalDecayEnabled)
                {
                    DateTime referenceTimestamp = DateTime.UtcNow;
                    _logger.LogInformation($"Temporal decay enabled. Using {referenceTimestamp} as the reference timestamp.");
                    // Apply exponential decay to final RRF scores
                    foreach (var result in combinedResults)
                    {
                        double decayMultiplier = ApplyExponentialDecay(result.Timestamp, referenceTimestamp, decayImpact);
                        result.Score *= decayMultiplier;
                        _logger.LogInformation($"Score for result {result.FileName}_{result.ChunkIndex} after decay: {result.Score} (multiplier={decayMultiplier})");
                        

                    }
                    combinedResults = combinedResults
                        .OrderByDescending(r => r.Score)
                        .ToList();
                }

                // Limit to requested number of results
                return combinedResults.Take(nResults ?? 10).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during hybrid search.");
                return new List<EmbeddingSearchResult>();
            }
        }

        /// <summary>
        /// Primary method for semantic search in collection manager
        /// </summary>
        /// <param name="query">Query string</param>
        /// <param name="collectionName">Name of the collection to search</param>
        /// <param name="nResults">Number of results to return</param>
        /// <param name="timeWindowEnabled">Whether to enable time-window filtering</param>
        /// <param name="windowDays">Number of days to include in the time window</param>
        /// /// <param name="temporalDecayEnabled">Whether to enable temporal decay</param>
        /// <param name="decayImpact">Impact of temporal decay</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of search results</returns>
        /// <exception cref="InvalidOperationException">Thrown when the collection does not exist</exception>
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
            try
            {
                List<EmbeddingSearchResult> results;
                // Check to make sure that the collection exists
                bool collectionExists = await _schemaManagementService.CheckCollectionExists(collectionName);
                if (!collectionExists)
                {
                    throw new InvalidOperationException($"Collection named {collectionName} does not exist.");
                }
                
                // Get embedding service information
                string embeddingServiceId;
                string embeddingServiceModality;
                (embeddingServiceId, embeddingServiceModality) = await _schemaManagementService.getDefaultEmbeddingServiceDetails(collectionName);
                _logger.LogInformation($"Embedding model {embeddingServiceId} is valid for collection {collectionName}");
                
                // Calculate the time window cutoff date if time-window filtering is enabled
                DateTime? cutoffDate = null;
                if (timeWindowEnabled)
                {
                    cutoffDate = DateTime.UtcNow.AddDays(-windowDays);
                    _logger.LogInformation($"Time-window filtering enabled. Only including results after {cutoffDate}.");
                }

                results = await BM25SearchAsync(
                    collectionName,
                    query,
                    nResults,
                    timeWindowEnabled,
                    windowDays,
                    temporalDecayEnabled,
                    decayImpact,
                    cancellationToken
                );

                if (temporalDecayEnabled)
                {
                    DateTime referenceTimestamp = DateTime.UtcNow;
                    _logger.LogInformation($"Temporal decay enabled. Using {referenceTimestamp} as the reference timestamp.");
                    // Apply exponential decay to final RRF scores
                    foreach (var result in results)
                    {
                        double decayMultiplier = ApplyExponentialDecay(result.Timestamp, referenceTimestamp, decayImpact);
                        result.Score *= decayMultiplier;
                        _logger.LogInformation($"Score for result {result.FileName}_{result.ChunkIndex} after decay: {result.Score} (multiplier={decayMultiplier})");
                    }
                    results = results
                        .OrderByDescending(r => r.Score)
                        .ToList();
                }
                
                // Limit to requested number of results
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during hybrid search.");
                return new List<EmbeddingSearchResult>();
            }
        }

        /// <summary>
        /// Hybrid search for fixed collection
        /// </summary>
        /// <param name="query"></param>
        /// <param name="collectionName"></param>
        /// <param name="nResults"></param>
        /// <param name="alpha"></param>
        /// <param name="timeWindowEnabled"></param>
        /// <param name="windowDays"></param>
        /// <param name="temporalDecayEnabled"></param>
        /// <param name="decayImpact"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
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
                // Check to make sure that the collection exists
                bool collectionExists = await _schemaManagementService.CheckCollectionExists(collectionName);
                if (!collectionExists)
                {
                    throw new InvalidOperationException($"Collection named {collectionName} does not exist.");
                }
                
                ThreadPool.GetAvailableThreads(out int availableWorkerThreads, out int availableCompletionPortThreads);
                ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int maxCompletionPortThreads);

                Console.WriteLine($"Available worker threads: {availableWorkerThreads}/{maxWorkerThreads}");

                // Perform hybrid search
                var results = await HybridSearchAsync(
                    query,
                    collectionName,
                    nResults,
                    alpha,
                    timeWindowEnabled,
                    windowDays,
                    temporalDecayEnabled,
                    decayImpact,
                    cancellationToken
                );
                
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while performing hybrid search on the fixed collection.");
                return new List<EmbeddingSearchResult>();
            }
        }
        
        /// <summary>
        /// Image search is not supported for text collections.
        /// </summary>
        /// <param name="query">Image file to use as a query</param>
        /// <param name="collectionName">Name of the collection to search</param>
        /// <param name="nResults">Number of results to return</param>
        /// <param name="timeWindowEnabled">Whether to enable time-window filtering</param>
        /// <param name="windowDays">Number of days to include in the time window</param>
        /// <param name="temporalDecayEnabled">Whether to enable temporal decay</param>
        /// <param name="decayImpact">Impact of temporal decay</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Empty list of search results</returns>
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
            // Log that image search is not supported for text collections
            _logger.LogWarning("Image search is not supported for text collection {CollectionName}", collectionName);
            
            // Throw NotImplementedException to indicate this functionality is intentionally not supported
            // This will be caught by the controller and returned as a 501 Not Implemented response
            throw new NotImplementedException("Image search is not supported for text collections. Use TextSearchFixedCollectionAsync instead.");
        }
    }
}
