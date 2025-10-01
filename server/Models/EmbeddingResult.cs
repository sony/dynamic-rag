namespace PgVectorDynamicRAG.Models
{
    /// <summary>
    /// Definition for the embedding results
    /// </summary>
    public class EmbeddingResult
    {
        /// <summary>
        /// Gets or sets the unique identifier for the embedding result.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the vector representation of the embedding result as an array of floats.
        /// </summary>
        public float[] Vector { get; set; }
    }
}
