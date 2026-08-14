using Microsoft.AspNetCore.Mvc;
using PgVectorDynamicRAG.Data;
using PgVectorDynamicRAG.Services;
using Microsoft.AspNetCore.Http;

namespace PgVectorDynamicRAG.Controllers
{
    /// <summary>
    /// The CollectionManagementController uses services to manage data within collections. This includes
    /// functions to injest data from files, remove data by file, and search for data in collections.
    /// Search methods include vector search, semantic search, and hybrid searches to retreive data
    /// that most simiarly matches input strings.
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class CollectionManagementController : ControllerBase
    {
        private readonly CollectionManagementService _collectionManagementService;
        /// <summary>
        /// Constructor for the CollectionManagementController
        /// </summary>
        /// <param name="collectionManagementService"></param>
        /// <param name="connectionFactory"></param>
        public CollectionManagementController(CollectionManagementService collectionManagementService, IPostgresConnectionFactory connectionFactory)
        {
            _collectionManagementService = collectionManagementService;
        }

        /// <summary>
        /// Route to the /create-collection GET endpoint
        /// 
        /// This endpoint creates a new collection in the vector database with the given name
        /// and the default embedding model. The default embedding model is used to create the
        /// collection with the correct dimensionality and indexing method.
        /// request.defaultEmbeddingModelName (string) - the name of the default embedding model
        /// request.collectionName (string) - the name of the collection to create
        /// request.initHNSW (bool) - whether to initialize the collection with HNSW indexing
        /// request.initBTree (bool) - whether to initialize the collection with BTree indexing
        /// </summary>
        [HttpPost("create-collection")]
        public async Task<IActionResult> CreateCollection([FromBody] CreateCollectionRequest request)
        {
            try
            {
                var safeCollectionName = SqlIdentifierValidator.Validate(request.collectionName, nameof(request.collectionName));
                var startTime = DateTime.UtcNow;

                Dictionary<string, object> result = await _collectionManagementService.CreateCollection(
                                                                                                    request.embeddingType,
                                                                                                    request.defaultEmbeddingModelName,
                                                                                                    safeCollectionName,
                                                                                                    request.description ?? "",
                                                                                                    request.initHNSW,
                                                                                                    request.initBTree
                                                                                                    );

                var endTime = DateTime.UtcNow;

                result["internal_execution_time"] = (endTime - startTime).TotalSeconds;

                return Ok(result);
            }
            catch (NotImplementedException ex)
            {
                // Return 501 Not Implemented status code
                return StatusCode(StatusCodes.Status501NotImplemented, new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message, title: "Failed to process the request", statusCode: StatusCodes.Status500InternalServerError, 
                    extensions: new Dictionary<string, object?> { { "success", false }, { "message", ex.Message } });
            }
        }

        /// <summary>
        /// Route to the /update-index GET endpoint
        /// 
        /// This endpoint updates the index of a collection in the vector database with the given name.
        /// This operation should be run occasionally. (once per day or week) in order to update the graph
        /// structure of the collection's HNSW index to improve performance.
        /// request.collectionName (string) - the name of the collection to update
        /// </summary>
        [HttpPost("update-index")]
        public async Task<IActionResult> UpdateIndex([FromBody] UpdateIndexRequest request)
        {
            try
            {
                var safeCollectionName = SqlIdentifierValidator.Validate(request.collectionName, nameof(request.collectionName));
                var startTime = DateTime.UtcNow;

                Dictionary<string, object> result = await _collectionManagementService.UpdateIndex(safeCollectionName);

                var endTime = DateTime.UtcNow;

                result["internal_execution_time"] = (endTime - startTime).TotalSeconds;

                return Ok(result);
            }
            catch (NotImplementedException ex)
            {
                // Return 501 Not Implemented status code
                return StatusCode(StatusCodes.Status501NotImplemented, new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message, title: "Failed to process the request", statusCode: StatusCodes.Status500InternalServerError, 
                    extensions: new Dictionary<string, object?> { { "success", false }, { "message", ex.Message } });
            }
        }

        /// <summary>
        /// Route to the /delete-collection GET endpoint
        /// 
        /// This endpoint deletes a collection from the vector database with the given name.
        /// request.collectionName (string) - the name of the collection to remove.
        /// </summary>
        [HttpPost("delete-collection")]
        public async Task<IActionResult> DeleteCollection([FromBody] DeleteCollectionRequest request)
        {
            try
            {
                var safeCollectionName = SqlIdentifierValidator.Validate(request.collectionName, nameof(request.collectionName));
                var startTime = DateTime.UtcNow;

                Dictionary<string, object> result = await _collectionManagementService.DeleteCollection(safeCollectionName,
                                                                                                    request.deleteBlobs);

                var endTime = DateTime.UtcNow;

                result["internal_execution_time"] = (endTime - startTime).TotalSeconds;

                return Ok(result);
            }
            catch (NotImplementedException ex)
            {
                // Return 501 Not Implemented status code
                return StatusCode(StatusCodes.Status501NotImplemented, new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message, title: "Failed to process the request", statusCode: StatusCodes.Status500InternalServerError, 
                    extensions: new Dictionary<string, object?> { { "success", false }, { "message", ex.Message } });
            }
        }

        /// <summary>
        /// Route to the /ingest-file POST endpoint
        /// 
        /// This endpoint takes a filepath to either a local, blob,
        /// or S3 and ingests the data into a named collection in the vector database.
        /// File data is chunked according to the file type and size of the input data.
        /// request.collectionName (string) - collection/table to ingest the data into
        /// request.chunkSize (int) - primary chunk size to split the data into
        /// request.chunkOverlapFraction (float) - fraction of the chunk size to overlap
        /// request.filePath (string) - path to the file to ingest
        /// </summary>
        [HttpPost("ingest-file")]
        public async Task<IActionResult> IngestFileToCollectionAsync([FromForm] EmbedNewFileRequest request)
        {
            try
            {
                Console.WriteLine("Received file upload request");
                Console.WriteLine($"Request received: {System.Text.Json.JsonSerializer.Serialize(request)}");
                var startTime = DateTime.UtcNow;

                Dictionary<string, object> result;
                
                // Validate required parameters
                if (string.IsNullOrEmpty(request.collectionName))
                {
                    return BadRequest("Collection name is required");
                }

                var safeCollectionName = SqlIdentifierValidator.Validate(request.collectionName, nameof(request.collectionName));

                // Determine the source type and process accordingly
                if (request.file != null)
                {
                    // Case 1: File was uploaded directly
                    result = await _collectionManagementService.IngestUploadedFileAsync(
                        safeCollectionName,
                        request.chunkSize,
                        request.chunkOverlapFraction,
                        request.file,
                        request.saveToBlobStorage // Whether to save a copy to blob storage
                    );
                }
                else if (!string.IsNullOrEmpty(request.filePath))
                {
                    // Case 2: Local file path on server was provided
                    result = await _collectionManagementService.IngestLocalFileAsync(
                        safeCollectionName,
                        request.chunkSize,
                        request.chunkOverlapFraction,
                        request.filePath,
                        request.saveToBlobStorage // Whether to save a copy to blob storage
                    );
                }
                else
                {
                    return BadRequest("Either a file upload or a local file path must be provided");
                }

                var endTime = DateTime.UtcNow;
                result["internal_execution_time"] = (endTime - startTime).TotalSeconds;

                return Ok(result);

            }
            catch (NotImplementedException ex)
            {
                // Return 501 Not Implemented status code
                return StatusCode(StatusCodes.Status501NotImplemented, new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message, title: "Failed to process the request", statusCode: StatusCodes.Status500InternalServerError, 
                    extensions: new Dictionary<string, object?> { { "success", false }, { "message", ex.Message } });
            }
        }

        /// <summary>
        /// Route to the /ingest-text POST endpoint
        /// 
        /// This endpoint takes a HTTP post request with a string to ingest into a collection as a memory
        /// request.collectionName (string) - collection/table to ingest the data into
        /// request.chunkSize (int) - primary chunk size to split the data into
        /// request.chunkOverlapFraction (float) - fraction of the chunk size to overlap
        /// request.content (string) - content to ingest
        /// </summary>
        [HttpPost("new-text-memory")]
        public async Task<IActionResult> IngestTextToCollectionAsync([FromForm] EmbedNewTextRequest request)
        {
            try
            {
                Console.WriteLine("Received text upload request");
                Console.WriteLine($"Request received: {System.Text.Json.JsonSerializer.Serialize(request)}");
                var startTime = DateTime.UtcNow;

                Dictionary<string, object> result;
                
                // Validate required parameters
                if (string.IsNullOrEmpty(request.collectionName))
                {
                    return BadRequest("Collection name is required");
                }

                var safeCollectionName = SqlIdentifierValidator.Validate(request.collectionName, nameof(request.collectionName));

                // Determine the source type and process accordingly
                if (request.content != null)
                {
                    // Case 1: File was uploaded directly
                    result = await _collectionManagementService.NewTextMemoryAsync(
                        safeCollectionName,
                        request.chunkSize,
                        request.chunkOverlapFraction,
                        request.content,
                        request.saveToBlobStorage
                    );
                }
                else
                {
                    return BadRequest("Content must be provided");
                }

                var endTime = DateTime.UtcNow;
                result["internal_execution_time"] = (endTime - startTime).TotalSeconds;

                return Ok(result);

            }
            catch (NotImplementedException ex)
            {
                // Return 501 Not Implemented status code
                return StatusCode(StatusCodes.Status501NotImplemented, new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message, title: "Failed to process the request", statusCode: StatusCodes.Status500InternalServerError, 
                    extensions: new Dictionary<string, object?> { { "success", false }, { "message", ex.Message } });
            }
        }

        /// <summary>
        /// Route to the /remove-file-from-collection POST endpoint
        /// 
        /// This endpoint takes a file name and collection name and removes the file from the collection.
        /// request.collectionName (string) - collection/table to remove the data from
        /// request.filePath (string) - path to the file to remove
        /// </summary>
        [HttpPost("remove-file-from-collection")]
        public async Task<IActionResult> RemoveFileFromCollectionAsync([FromBody] RemoveFileFromCollectionRequest request)
        {
            try
            {
                var safeCollectionName = SqlIdentifierValidator.Validate(request.collectionName, nameof(request.collectionName));
                var startTime = DateTime.UtcNow;

                Dictionary<string, object> result = await _collectionManagementService.RemoveFileFromCollectionAsync(request.pathInContainer,
                                                                                                                    safeCollectionName,
                                                                                                                    request.isBlobFile
                                                                                                                    );

                var endTime = DateTime.UtcNow;

                result["internal_execution_time"] = (endTime - startTime).TotalSeconds;

                return Ok(result);
            }
            catch (NotImplementedException ex)
            {
                // Return 501 Not Implemented status code
                return StatusCode(StatusCodes.Status501NotImplemented, new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return Problem($"Failed to remove the file: {request.pathInContainer}, from collection: {request.collectionName}, due to error: {ex.Message}");
            }
        }

        /// <summary>
        /// Route to the /text-vector-search-collection POST endpoint
        /// 
        /// Searches a named collection using only vector search.
        /// </summary>
        [HttpPost("text-vector-search-collection")]
        public async Task<IActionResult> TextVectorSearchFixedCollectionAsync([FromBody] TextSearchFixedCollectionRequest request)
        {
            try
            {
                var safeCollectionName = SqlIdentifierValidator.Validate(request.collectionName, nameof(request.collectionName));
                var startTime = DateTime.UtcNow;

                Dictionary<string, object> result = new Dictionary<string, object>();

                List<EmbeddingSearchResult> searchResults = await _collectionManagementService.TextVectorSearchFixedCollectionAsync(
                                                                                                                    request.query,
                                                                                                                    safeCollectionName,
                                                                                                                    request.nResults,
                                                                                                                    request.timeWindowEnabled,
                                                                                                                    request.windowDays,
                                                                                                                    request.temporalDecayEnabled,
                                                                                                                    request.decayImpact,
                                                                                                                    default
                                                                                                                    );

                var endTime = DateTime.UtcNow;
                result["internal_execution_time"] = (endTime - startTime).TotalSeconds;
                result["success"] = true;
                result["search_results"] = searchResults;
                return Ok(result);
            }
            catch (NotImplementedException ex)
            {
                // Return 501 Not Implemented status code
                return StatusCode(StatusCodes.Status501NotImplemented, new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message, title: "Failed to process the request", statusCode: StatusCodes.Status500InternalServerError, 
                    extensions: new Dictionary<string, object?> { { "success", false }, { "message", ex.Message } });
            }
        }

        /// <summary>
        /// Route to the /text-semantic-search-collection POST endpoint
        /// 
        /// Semantic searches a named collection using only semantic search
        /// </summary>
        [HttpPost("text-semantic-search-collection")]
        public async Task<IActionResult> TextSemanticSearchFixedCollectionAsync([FromBody] TextSearchFixedCollectionRequest request)
        {
            try
            {
                var safeCollectionName = SqlIdentifierValidator.Validate(request.collectionName, nameof(request.collectionName));
                var startTime = DateTime.UtcNow;

                Dictionary<string, object> result = new Dictionary<string, object>();

                List<EmbeddingSearchResult> searchResults = await _collectionManagementService.TextSemanticSearchFixedCollectionAsync(
                                                                                                                    request.query,
                                                                                                                    safeCollectionName,
                                                                                                                    request.nResults,
                                                                                                                    request.timeWindowEnabled,
                                                                                                                    request.windowDays,
                                                                                                                    request.temporalDecayEnabled,
                                                                                                                    request.decayImpact,
                                                                                                                    default
                                                                                                                    );

                var endTime = DateTime.UtcNow;

                result["internal_execution_time"] = (endTime - startTime).TotalSeconds;
                result["success"] = true;
                result["search_results"] = searchResults;
                return Ok(result);
            }
            catch (NotImplementedException ex)
            {
                // Return 501 Not Implemented status code
                return StatusCode(StatusCodes.Status501NotImplemented, new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message, title: "Failed to process the request", statusCode: StatusCodes.Status500InternalServerError, 
                    extensions: new Dictionary<string, object?> { { "success", false }, { "message", ex.Message } });
            }
        }

        /// <summary>
        /// Route to the /text-search-collection POST endpoint
        /// 
        /// Searches a named collection using text input (query parameter). For TextEmbedding collections, we use hybrid search
        /// as the default mode. For ImageEmbeddings, we do a text-to-image search on the collection.
        /// </summary>
        [HttpPost("text-search-collection")]
        public async Task<IActionResult> TextSearchFixedCollectionAsync([FromBody] TextSearchFixedCollectionRequest request)
        {
            try
            {
                var safeCollectionName = SqlIdentifierValidator.Validate(request.collectionName, nameof(request.collectionName));
                var startTime = DateTime.UtcNow;

                Dictionary<string, object> result = new Dictionary<string, object>();

                List<EmbeddingSearchResult> searchResults = await _collectionManagementService.TextSearchFixedCollectionAsync(
                                                                                                                    request.query,
                                                                                                                    safeCollectionName,
                                                                                                                    request.nResults,
                                                                                                                    request.alpha,
                                                                                                                    request.timeWindowEnabled,
                                                                                                                    request.windowDays,
                                                                                                                    request.temporalDecayEnabled,
                                                                                                                    request.decayImpact,
                                                                                                                    default
                                                                                                                    );

                var endTime = DateTime.UtcNow;

                result["internal_execution_time"] = (endTime - startTime).TotalSeconds;
                result["success"] = true;
                result["search_results"] = searchResults;
                return Ok(result);
            }
            catch (NotImplementedException ex)
            {
                // Return 501 Not Implemented status code
                return StatusCode(StatusCodes.Status501NotImplemented, new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message, title: "Failed to process the request", statusCode: StatusCodes.Status500InternalServerError, 
                    extensions: new Dictionary<string, object?> { { "success", false }, { "message", ex.Message } });
            }
        }

        /// <summary>
        /// Route to the /image-search-collection POST endpoint
        /// 
        /// Searches a named collection using image input (query parameter). For TextEmbedding collections, we return a NotImplementedError
        /// For ImageEmbeddings collections, we do an image-to-image search. 
        /// </summary>
        [HttpPost("image-search-collection")]
        public async Task<IActionResult> ImageSearchFixedCollectionAsync([FromForm] ImageSearchFixedCollectionRequest request)
        {
            try
            {
                var safeCollectionName = SqlIdentifierValidator.Validate(request.collectionName, nameof(request.collectionName));
                var startTime = DateTime.UtcNow;

                Dictionary<string, object> result = new Dictionary<string, object>();

                List<EmbeddingSearchResult> searchResults = await _collectionManagementService.ImageSearchFixedCollectionAsync(
                                                                                                                    request.image,
                                                                                                                    safeCollectionName,
                                                                                                                    request.nResults,
                                                                                                                    request.timeWindowEnabled,
                                                                                                                    request.windowDays,
                                                                                                                    request.temporalDecayEnabled,
                                                                                                                    request.decayImpact,
                                                                                                                    default
                                                                                                                    );

                var endTime = DateTime.UtcNow;

                result["internal_execution_time"] = (endTime - startTime).TotalSeconds;
                result["success"] = true;
                result["search_results"] = searchResults;
                return Ok(result);
            }
            catch (NotImplementedException ex)
            {
                // Return 501 Not Implemented status code
                return StatusCode(StatusCodes.Status501NotImplemented, new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message, title: "Failed to process the request", statusCode: StatusCodes.Status500InternalServerError, 
                    extensions: new Dictionary<string, object?> { { "success", false }, { "message", ex.Message } });
            }
        }
    }
}
