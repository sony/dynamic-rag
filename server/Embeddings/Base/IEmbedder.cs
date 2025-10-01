using PgVectorDynamicRAG.Models;

namespace PgVectorDynamicRAG.Embeddings.Base
{
    /// <summary>
    /// Represents a contract for generating embeddings for a specific data type.
    /// </summary>
    /// <typeparam name="TModel">
    /// The type of the input model for which the embedding will be generated.
    /// </typeparam>
    public interface IEmbedder<TModel>
    {
        /// <summary>
        /// Generate embeedings for PgVectorRepository
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        Task<EmbeddingResult> GenerateEmbeddingAsync(TModel input);
    }
}
