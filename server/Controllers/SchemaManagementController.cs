using Microsoft.AspNetCore.Mvc;
using PgVectorDynamicRAG.Data;
using PgVectorDynamicRAG.Services;

namespace PgVectorDynamicRAG.Controllers
{
    /// <summary>
    /// The SchemaManagementController uses the SchemaManagementService to maintain the
    /// core schema of the vector database. This includes functions to manage the metadata on the collections,
    /// embedding models, and generative models, so that collection and model.
    /// This controller primary contains functions to create and delete collections,
    /// but also services some functions requited for the initialization of the vector database.
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class SchemaManagementController : ControllerBase
    {
        private readonly SchemaManagementService _schemaManagementService;
        /// <summary>
        /// Constructor for the schema management controller object
        /// </summary>
        /// <param name="schemaManagementService"></param>
        public SchemaManagementController(SchemaManagementService schemaManagementService)
        {
            _schemaManagementService = schemaManagementService;
        }

        /// <summary>
        /// Route to the /read-chat-models GET endpoint
        /// </summary>
        /// <returns></returns>
        [HttpGet("read-chat-models")]
        public async Task<IActionResult> ReadChatModels()
        {
            try
            {
                var startTime = DateTime.UtcNow;

                List<string> chatModels = await _schemaManagementService.ReadChatModels();

                var endTime = DateTime.UtcNow;

                Dictionary<string, object> result = new Dictionary<string, object>();
                result["internal_execution_time"] = (endTime - startTime).TotalSeconds;
                result["chat_models"] = chatModels;

                return Ok(result);
            }
            catch (Exception ex)
            {
                return Problem($"Failed to read chat models: {ex.Message}");
            }
        }
    
        /// <summary>
        /// Route to the /read-embedding-models GET endpoint
        /// </summary>
        /// <returns></returns>
        [HttpGet("read-embedding-models")]
        public async Task<IActionResult> ReadEmbeddingModels()
        {
            try
            {
                var startTime = DateTime.UtcNow;

                Dictionary<string, List<string>> embeddingModels = await _schemaManagementService.ReadEmbeddingModels();

                var endTime = DateTime.UtcNow;

                Dictionary<string, object> result = new Dictionary<string, object>();
                result["internal_execution_time"] = (endTime - startTime).TotalSeconds;
                result["embedding_models"] = embeddingModels;

                return Ok(result);
            }
            catch (Exception ex)
            {
                return Problem($"Failed to read embedding models: {ex.Message}");
            }
        }

        /// <summary>
        /// Route to the /read-collections GET endpoint
        /// </summary>
        /// <returns>Collection details including name, embedding service ID, and distance metric</returns>
        [HttpGet("read-collections")]
        public async Task<IActionResult> ReadCollections()
        {
            try
            {
                var startTime = DateTime.UtcNow;

                List<Dictionary<string, string>> collections = await _schemaManagementService.ReadCollections();

                var endTime = DateTime.UtcNow;

                Dictionary<string, object> result = new Dictionary<string, object>();
                result["internal_execution_time"] = (endTime - startTime).TotalSeconds;
                result["collections"] = collections;

                return Ok(result);
            }
            catch (Exception ex)
            {
                return Problem($"Failed to read collections: {ex.Message}");
            }
        }
    }
}