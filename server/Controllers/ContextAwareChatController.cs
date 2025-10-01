using Microsoft.AspNetCore.Mvc;
using PgVectorDynamicRAG.Data;
using PgVectorDynamicRAG.Services;
// using System;
// using System.Threading.Tasks;

namespace PgVectorDynamicRAG.Controllers
{
    /// <summary>
    /// The ContextAwareChatController uses services to chat with data from collections. This includes
    /// functions to chat with data from collections and return the most relevant results.
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class ContextAwareChatController : ControllerBase
    {
        private readonly ContextAwareChatService _contextAwareChatService;
        /// <summary>
        /// Constructor for the ContextAwareChatController
        /// </summary>
        /// <param name="contextAwareChatService"></param>
        /// <param name="connectionFactory"></param>
        public ContextAwareChatController(ContextAwareChatService contextAwareChatService, IPostgresConnectionFactory connectionFactory)
        {
            _contextAwareChatService = contextAwareChatService;
        }

        /// <summary>
        /// Route to the /search-collection POST endpoint
        /// 
        /// This endpoint takes a query string and collection name and returns a response
        /// from the LLM with the most relevant results as context to the response.
        /// request.query - the query string to search for
        /// request.collectionName - collection/table to search in
        /// request.nResults - number of results to return
        /// request.chatModelName - name of the chat model to use
        /// </summary>
        [HttpPost("chat-with-collection")]
        public async Task<IActionResult> ChatWithFixedCollectionAsync([FromBody] ChatWithCollectionRequest request)
        {
            try
            {
                var startTime = DateTime.UtcNow;

                Dictionary<string, object> result = new Dictionary<string, object>();

                ContextAwareChatResponse chatResult = await _contextAwareChatService.ChatWithFixedCollectionAsync(
                                                                                                                request.query,
                                                                                                                request.collectionName,
                                                                                                                request.nResults,
                                                                                                                request.chatModelName,
                                                                                                                request.alpha,
                                                                                                                request.timeWindowEnabled,
                                                                                                                request.windowDays,
                                                                                                                request.temporalDecayEnabled,
                                                                                                                request.decayImpact
                                                                                                            );

                var endTime = DateTime.UtcNow;

                result["internal_execution_time"] = (endTime - startTime).TotalSeconds;
                result["success"] = true;
                result["search_results"] = chatResult.SearchResult;
                result["query"] = chatResult.Query;
                result["final_answer"] = chatResult.FinalAnswer;
                return Ok(result);
            }
            catch (Exception ex)
            {
                return Problem($"Failed to chat with data from collection: {ex.Message}");
            }
        }
    }
}
