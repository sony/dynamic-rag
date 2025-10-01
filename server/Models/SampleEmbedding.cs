using System.ComponentModel.DataAnnotations;
using NpgsqlTypes;

namespace PgVectorDynamicRAG.Models
{
    /// <summary>
    /// Represents a sample embedding entity used in the PgVectorDynamicRAG application.
    /// </summary>
    public class SampleEmbedding
    {
        /// <summary>
        /// Gets or sets the unique identifier for the sample embedding.
        /// </summary>
        [Key]
        public long Id { get; set; }
        /// <summary>
        /// Gets or sets the embedding vector represented as an NpgsqlTsVector.
        /// This is used for storing and querying vector data in PostgreSQL.
        /// </summary>
        public NpgsqlTsVector embedding { get; set; } = default!;
    }
}
