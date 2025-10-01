using Microsoft.EntityFrameworkCore;
using PgVectorDynamicRAG.Models;
using NpgsqlTypes;

namespace PgVectorDynamicRAG.Data
{
    /// <summary>
    /// The ApplicationDbContext class is used to create a connection to the PostgreSQL database
    /// </summary>
    public class ApplicationDbContext : DbContext
    {
        /// <summary>
        /// contructor for the DbContextOptions
        /// </summary>
        /// <param name="options"></param>
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }
        /// <summary>
        /// define the method for sample embeddings generation
        /// </summary>
        public DbSet<SampleEmbedding> sample_embeddings_dotnet { get; set; }
    }
}
