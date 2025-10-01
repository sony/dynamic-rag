using PgVectorDynamicRAG.Models;

namespace PgVectorDynamicRAG.Embeddings.Base
{
    /// <summary>
    /// Represents the base class for embedding logic, providing a foundation for implementing
    /// embedding generation functionality. This class can be extended to include shared logic
    /// for embedding operations or left as is if no shared logic is required.
    /// </summary>
    /// <typeparam name="TModel">
    /// The type of the input model for which embeddings will be generated.
    /// </typeparam>
    public abstract class EmbedderBase<TModel> : IEmbedder<TModel>
    {
        /// <summary>
        /// Asynchronously generates an embedding for the specified input model.
        /// This method can be overridden in derived classes to provide specific
        /// embedding generation logic.
        /// </summary>
        /// <param name="input">The input model for which the embedding is to be generated.</param>
        /// <returns>
        /// A <see cref="Task{TResult}"/> representing the asynchronous operation,
        /// with a result of type <see cref="EmbeddingResult"/> containing the generated embedding.
        /// </returns>
        /// <exception cref="System.NotImplementedException">
        /// Thrown if the method is not overridden in a derived class and no default implementation is provided.
        /// </exception>
        public virtual Task<EmbeddingResult> GenerateEmbeddingAsync(TModel input)
        {
            // Optionally implement default logic or throw NotImplementedException
            throw new System.NotImplementedException();
        }
        
        // Other common helper methods, e.g., for scaling vectors or logging
    }
}
