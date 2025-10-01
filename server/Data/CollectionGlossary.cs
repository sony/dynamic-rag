using Microsoft.Extensions.VectorData;           // <-- Important for IVectorStore, VectorSearchFilter, etc.

namespace PgVectorDynamicRAG.Data
{
    /// <summary>
    /// Base class that defines common properties for glossary records.
    /// </summary>
    public abstract class CollectionGlossaryBase
    {
        /// <summary>
        /// Gets or sets the unique identifier for the glossary record.
        /// </summary>
        [VectorStoreKey]
        public Guid Key { get; set; }
        /// <summary>
        /// Gets or sets the file name of the glossary record.
        /// </summary>
        [VectorStoreData(IsIndexed = true)]
        public string FileName { get; set; }
        /// <summary>
        /// Gets or sets the page identifier for the glossary record.
        /// </summary>
        [VectorStoreData(IsIndexed = true)]
        public int PageId { get; set; }
        /// <summary>
        /// Gets or sets the chunk index for the glossary record.
        /// </summary>
        [VectorStoreData]
        public int ChunkIndex { get; set; }
        /// <summary>
        /// Gets or sets the definition for the glossary record.
        /// </summary>
        [VectorStoreData]
        public string Definition { get; set; }
        /// <summary>
        /// Gets or sets the embedding vector for the glossary record.
        /// </summary>
        [VectorStoreVector(1536)]
        public ReadOnlyMemory<float> DefinitionEmbedding { get; set; }
        /// <summary>
        /// Gets or sets the embedding service identifier for the glossary record.
        /// </summary>
        [VectorStoreData]
        public string EmbeddingServiceId { get; set; }
        /// <summary>
        /// Gets or sets the modality for the glossary record.
        /// </summary>
        [VectorStoreData]
        public string Modality { get; set; }
        /// <summary>
        /// Gets or sets the timestamp when the embedding was created.
        /// </summary>
        [VectorStoreData(IsIndexed = true)]
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Glossary type for an embedding model with 1536 dimensions.
    /// </summary>
    public sealed class GlossaryModelAda002 : CollectionGlossaryBase
    {
        /// <summary>
        /// Gets or sets the embedding vector for the glossary record.
        /// </summary>
        [VectorStoreVector(1536)]
        public ReadOnlyMemory<float> DefinitionEmbedding { get; set; }
    }

    /// <summary>
    /// Glossary type for an embedding model with 1536 dimensions.
    /// </summary>
    public sealed class GlossaryModelAda3sm : CollectionGlossaryBase
    {
        /// <summary>
        /// Gets or sets the embedding vector for the glossary record.
        /// </summary>
        [VectorStoreVector(1536)]
        public ReadOnlyMemory<float> DefinitionEmbedding { get; set; }
    }

    /// <summary>
    /// Glossary type for an embedding model with 3072 dimensions.
    /// </summary>
    public sealed class GlossaryModelAda3lg : CollectionGlossaryBase
    {
        /// <summary>
        /// Gets or sets the embedding vector for the glossary record.
        /// </summary>
        [VectorStoreVector(3072)]
        public ReadOnlyMemory<float> DefinitionEmbedding { get; set; }
    }

    /// <summary>
    /// Glossary type for an embedding model with 1024 dimensions.
    /// </summary>
    public sealed class GlossaryModelTitanTextEmbeddingV2 : CollectionGlossaryBase
    {
        /// <summary>
        /// Gets or sets the embedding vector for the glossary record.
        /// </summary>
        [VectorStoreVector(1024)]
        public ReadOnlyMemory<float> DefinitionEmbedding { get; set; }
    }


    /// <summary>
    /// Glossary type for a CLIP embedding model with 768 dimensions.
    /// </summary>
    public sealed class GlossaryModelOpenaiClip : CollectionGlossaryBase
    {
        /// <summary>
        /// Gets or sets the embedding vector for the glossary record.
        /// </summary>
        [VectorStoreVector(768)]
        public ReadOnlyMemory<float> DefinitionEmbedding { get; set; }
    }


    /// <summary>
    /// Base class for embedding chunks.
    /// </summary>
    public class EmbeddingChunkRecord
    {
        /// <summary>
        /// Gets or sets the unique identifier for the embedding chunk.
        /// </summary>
        [VectorStoreKey]
        public Guid Key { get; set; }
        /// <summary>
        /// Gets or sets the file name of the embedding chunk.
        /// </summary>
        [VectorStoreData(IsIndexed = true)]
        public string FileName { get; set; }
        /// <summary>
        /// Gets or sets the page identifier for the embedding chunk.
        /// </summary>
        [VectorStoreData(IsIndexed = true)]
        public int ChunkIndex { get; set; }
        /// <summary>
        /// Gets or sets the embedding vector for the embedding chunk.
        /// </summary>
        [VectorStoreData(IsIndexed = true)]
        public int PageId { get; set; }
        /// <summary>
        /// Gets or sets the embedding vector for the embedding chunk.
        /// </summary>
        [VectorStoreData]
        public string Definition { get; set; }

        // // The model that generated this embedding
        // [VectorStoreData(IsFilterable = true)]
        // public string ModelId { get; set; }

        /// <summary>
        /// Gets or sets the embedding vector for the embedding chunk.
        /// </summary>
        [VectorStoreVector(768)]
        public ReadOnlyMemory<float> DefinitionEmbedding { get; set; }
        /// <summary>
        /// Gets or sets the embedding service identifier for the embedding chunk.
        /// </summary>
        [VectorStoreData(IsIndexed = true)]
        public string EmbeddingServiceId { get; set; }
        /// <summary>
        /// Gets or sets the modality for the embedding chunk.
        /// </summary>
        [VectorStoreData(IsIndexed = true)]
        public string Modality { get; set; }
        /// <summary>
        /// Gets or sets the timestamp when the embedding was created.
        /// </summary>
        [VectorStoreData(IsIndexed = true)]
        public DateTime Timestamp { get; set; }
    }
}