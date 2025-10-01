using PgVectorDynamicRAG.Services;

namespace PgVectorDynamicRAG.Factories
{
    /// <summary>
    /// Factory class for creating embedding services based on modality.
    /// This class helps select the appropriate embedding service (text or image)
    /// based on the collection's modality.
    /// </summary>
    public class EmbeddingServiceFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly SchemaManagementService _schemaManagementService;

        /// <summary>
        /// Constructor for the embedding service factory.
        /// </summary>
        /// <param name="serviceProvider">Service provider for resolving dependencies</param>
        /// <param name="schemaManagementService">Schema management service for getting collection metadata</param>
        public EmbeddingServiceFactory(
            IServiceProvider serviceProvider,
            SchemaManagementService schemaManagementService)
        {
            _serviceProvider = serviceProvider;
            _schemaManagementService = schemaManagementService;
        }

        /// <summary>
        /// Get the appropriate embedding service for a collection based on its modality.
        /// </summary>
        /// <param name="collectionName">Name of the collection</param>
        /// <returns>The appropriate embedding service</returns>
        /// <exception cref="InvalidOperationException">Thrown if the collection doesn't exist or has an unsupported modality</exception>
        public async Task<AbstractEmbeddingService> GetEmbeddingServiceForCollectionAsync(string collectionName)
        {
            // Check if the collection exists
            bool collectionExists = await _schemaManagementService.CheckCollectionExists(collectionName);
            if (!collectionExists)
            {
                throw new InvalidOperationException($"Collection {collectionName} does not exist.");
            }

            // Get the embedding service ID and modality
            string embeddingServiceId;
            string embeddingServiceModality;
            (embeddingServiceId, embeddingServiceModality) = await _schemaManagementService.getDefaultEmbeddingServiceDetails(collectionName);

            // Return the appropriate service based on modality
            return embeddingServiceModality switch
            {
                "TextEmbedding" => _serviceProvider.GetRequiredService<TextEmbeddingService>(),
                "ImageEmbedding" => _serviceProvider.GetRequiredService<ImageEmbeddingService>(),
                _ => throw new InvalidOperationException($"Unsupported modality: {embeddingServiceModality}")
            };
        }

        /// <summary>
        /// Create a new embedding service based on the specified modality.
        /// This is useful when creating a new collection.
        /// </summary>
        /// <param name="modality">The modality to create a service for</param>
        /// <returns>The appropriate embedding service</returns>
        /// <exception cref="InvalidOperationException">Thrown if the modality is unsupported</exception>
        public AbstractEmbeddingService CreateEmbeddingServiceForModality(string modality)
        {
            return modality switch
            {
                "TextEmbedding" => _serviceProvider.GetRequiredService<TextEmbeddingService>(),
                "ImageEmbedding" => _serviceProvider.GetRequiredService<ImageEmbeddingService>(),
                _ => throw new InvalidOperationException($"Unsupported modality: {modality}")
            };
        }
    }
}
