using Microsoft.SemanticKernel;
using PgVectorDynamicRAG.Data;
using System.Text;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.PromptTemplates.Handlebars;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Functions;
using System.Linq;

#pragma warning disable SKEXP0001

namespace PgVectorDynamicRAG.Services
{
    /// <summary>
    /// Simple registry mapping each model ID to its vector dimension (and possibly other attributes).
    /// </summary>
    public class ContextAwareChatService
    {
        private readonly Kernel _kernel;
        private readonly ILogger<ContextAwareChatService> _logger;
        private readonly SchemaManagementService _schemaManagementService;

        private Task? _chatLoop;
        private readonly CollectionManagementService _collectionManagementService;

        /// <summary>
        /// Constructor for context aware chat
        /// </summary>
        /// <param name="kernel"></param>
        /// <param name="logger"></param>
        /// <param name="collectionManagementService"></param>
        /// <param name="schemaManagementService"></param>
        public ContextAwareChatService(Kernel kernel,
                                    ILogger<ContextAwareChatService> logger,
                                    CollectionManagementService collectionManagementService,
                                    SchemaManagementService schemaManagementService)
        {
            _kernel = kernel;
            _logger = logger;
            _collectionManagementService = collectionManagementService;
            _schemaManagementService = schemaManagementService;
        }


        // /// <summary>
        // /// Show how to create a <see cref="VectorStoreTextSearch{TRecord}"/> and use it to perform a search.
        // /// </summary>
        // // [Fact]
        // public async Task<ContextAwareChatResponse> ChatWithFixedCollectionAsync(string collectionName,
        //                                                 string query,
        //                                                 int nResults,
        //                                                 string chatModelName)
        // {
        //     List<EmbeddingSearchResult> searchResults = await _collectionManagementService.SearchFixedCollectionAsync(collectionName, query, 10);
        //     // List<SearchResult> rankedResults = (List<SearchResult>)searchResults["results"];
        //     foreach (var result in searchResults)
        //     {
        //         Console.WriteLine($"Rank:  {result.Rank}");
        //         Console.WriteLine($"Value: {result.Score}");
        //         Console.WriteLine($"Link:  {result.Definition}");
        //     }

        //     var chatService = _kernel.GetRequiredService<IChatCompletionService>();



        //     ContextAwareChatResponse finalResponse = new ContextAwareChatResponse(searchResults, query);
        //     return finalResponse;
        // }

        /// <summary>
        /// Searches a fixed collection for relevant documents, builds a RAG-style prompt with the search context,
        /// and then invokes the kernel’s prompt streaming API to generate an answer.
        /// </summary>
        /// <param name="collectionName">Name of the vector collection.</param>
        /// <param name="query">User’s query.</param>
        /// <param name="nResults">Number of search results to retrieve.</param>
        /// <param name="chatModelName">Name of the chat model to use (if applicable).</param>
        /// <param name="alpha">The weight of the temporal decay factor.</param>
        /// <param name="timeWindowEnabled">Whether to enable the time window filter.</param>
        /// <param name="windowDays">Number of days to include in the time window.</param>
        /// <param name="temporalDecayEnabled">Whether to enable the temporal decay factor.</param>
        /// <param name="decayImpact">The impact of the temporal decay factor.</param>
        /// <param name="cancellationToken">Cancellation token for the async operation.</param>
        /// <returns>A response containing the search results, original query, and the LLM’s answer.</returns>
        public async Task<ContextAwareChatResponse> ChatWithFixedCollectionAsync(
                                                                                string query,
                                                                                string collectionName,
                                                                                int nResults,
                                                                                string chatModelName,
                                                                                double alpha,
                                                                                bool timeWindowEnabled,
                                                                                int windowDays,
                                                                                bool temporalDecayEnabled,
                                                                                double decayImpact,
                                                                                CancellationToken cancellationToken = default
                                                                            )
        {
            // 1. Retrieve search results from the fixed collection.
            // List<EmbeddingSearchResult> searchResults = await _collectionManagementService.TextSearchFixedCollectionAsync(
            //                                                                                                 query,
            //                                                                                                 collectionName,
            //                                                                                                 nResults,
            //                                                                                                 alpha,
            //                                                                                                 timeWindowEnabled,
            //                                                                                                 windowDays,
            //                                                                                                 temporalDecayEnabled,
            //                                                                                                 decayImpact,
            //                                                                                                 cancellationToken
            //                                                                                             );
            List<EmbeddingSearchResult> searchResults = await _collectionManagementService.TextVectorSearchFixedCollectionAsync(
                                                                                                query,
                                                                                                collectionName,
                                                                                                nResults,
                                                                                                timeWindowEnabled,
                                                                                                windowDays,
                                                                                                temporalDecayEnabled,
                                                                                                decayImpact,
                                                                                                cancellationToken
                                                                                            );

            // Log search results (optional).
            // foreach (var result in searchResults)
            // {
            //     _logger.LogInformation("Rank: {Rank}, Score: {Score}, Link: {Link}",
            //         result.Rank, result.Score, result.Definition);
            // }

            // 2. Build a search context string from the search results.
            var searchContextBuilder = new StringBuilder();
            foreach (var result in searchResults)
            {
                searchContextBuilder.AppendLine($"Rank: {result.Rank}");
                // searchContextBuilder.AppendLine($"Score: {result.Score}");
                searchContextBuilder.AppendLine($"File name: {result.FileName}");
                searchContextBuilder.AppendLine($"Source data: {result.Definition}");
                searchContextBuilder.AppendLine("-----------------");
            }
            string searchContext = searchContextBuilder.ToString();

            // 3. Create the prompt template using Handlebars syntax.
//             var promptTemplate = @"
// Please use the following context to answer the question:
// {{searchContext}}

// Include citations to the relevant information where it is referenced in the response.

// Question: {{question}}";
            var promptTemplate = @"
            ### You are an AI information retreival agent.
            You are provided with the following context from various documents. Use **only** this context to answer the question.

            Context (for reference):
            {{searchContext}}

            Question:
            {{question}}

            1. **Relevance**:
                - If relevant context is available, provide an answer using the information given. Make sure to cite sources in-line using the format [1], [2], ... [N], where each [X] reference, 'X' is the rank of the corresponding document chunk from which the information was drawn. For example: [3] is a reference to the 3rd ranked citation.
                - If no relevant context is available response with: 'No relevant context available, please check that the file has been uploaded and indexed.', followed by a new line and a short explanation of search results that did come back and why they are not relevant.

            2. **Markdown Formatting**:
                - Structure your response with Markdown for readability and organization. Use elements such as:
                    - **Headings**: e.g., `## Heading`
                    - **Bulleted Lists**: e.g., `- Item 1`
                    - **Code Blocks**: e.g., ```python <code> ```
                - Ensure responses are easily renderable in Markdown and not encapsulated entirely in backticks or special markers.

            Now provide your final answer:
            ";


            // Prepare the prompt arguments.
            var kernelArguments = new KernelArguments()
            {
                { "question", query },
                { "searchContext", searchContext }
            };

            _logger.LogInformation("Invoking prompt response with chat model: {ChatModelName}", chatModelName);

            // 4. Get the validated service ID for the chat model deployment name
            string validatedServiceId = await _schemaManagementService.getChatServiceId(chatModelName, cancellationToken);
            _logger.LogInformation("Resolved chat model '{ChatModelName}' to service ID: {ServiceId}", chatModelName, validatedServiceId);

            // 5. Get the specific chat service by the validated service ID
            var chatService = _kernel.GetRequiredService<IChatCompletionService>(validatedServiceId);

            // 6. Use the chat service directly to generate the response
            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(promptTemplate);
            chatHistory.AddUserMessage($"Question: {query}\n\nContext: {searchContext}");

            var response = await chatService.GetChatMessageContentAsync(
                chatHistory,
                cancellationToken: cancellationToken
            );

            string finalAnswer = response.Content ?? string.Empty;

            _logger.LogInformation("Final Answer: {Answer}", finalAnswer);

            // 7. Return the aggregated response.
            return new ContextAwareChatResponse(searchResults, query, finalAnswer);
        }
    }
}
