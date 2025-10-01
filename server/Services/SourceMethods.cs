using PgVectorDynamicRAG.Models;
using PgVectorDynamicRAG.Data;
using NpgsqlTypes;
// Using Npgsql (#C) example shown here: https://github.com/pgvector/pgvector-dotnet?tab=readme-ov-file
using Npgsql;
using Pgvector;
using DotNetEnv;

namespace PgVectorDynamicRAG.Source
{
    /// <summary>
    /// Builds and returns an NpgsqlConnection asynchronously.
    /// </summary>
    public class ConnectionService {
        /// <summary>
        /// Builds and returns an NpgsqlConnection asynchronously.
        /// </summary>
        /// <param name="builder">The WebApplicationBuilder instance used to configure the application.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the NpgsqlConnection.</returns>
        public async Task<NpgsqlConnection> BuildDataSourceAsync(WebApplicationBuilder builder)
            {
                DotNetEnv.Env.Load();
                // POSTGRESQL_CONNECTION_STRING=Host=<HOST>;Database=<DB>;Username=<UN>;Password=<PW>
                var host = Environment.GetEnvironmentVariable("PG_HOST");
                var database = Environment.GetEnvironmentVariable("PG_DATABASE");
                var username = Environment.GetEnvironmentVariable("PG_USER");
                var password = Environment.GetEnvironmentVariable("PG_PASSWORD");
                // var connectionString = Environment.GetEnvironmentVariable("POSTGRESQL_CONNECTION_STRING");
                string connectionString = $"Host={host};Database={database};Username={username};Password={password}";
                var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
                dataSourceBuilder.UseVector();
                await using var dataSource = dataSourceBuilder.Build();

                var conn = dataSource.OpenConnection();
                conn.ReloadTypes();
                return conn;
            }
    }

    /// <summary>
    /// defines the embedding service
    /// </summary>
    public class EmbeddingService {
        /// <summary>
        /// Method to create sample embeddings.
        /// </summary>
        /// <param name="conn">The NpgsqlConnection object.</param>
        /// <param name="dimension">The dimension of the embeddings.</param>
        /// <param name="nEmbeddings">The number of embeddings to create.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task CreateSampleEmbeddings(NpgsqlConnection conn, int dimension, int nEmbeddings)
        {
            for (int i = 0; i < nEmbeddings; i++)
            {
                await using (var insertCmd = new NpgsqlCommand("INSERT INTO sample_embeddings_dotnet (embedding) VALUES ($1)", conn))
                {
                    var random = new Random();
                    var embeddingValues = new float[dimension];
                    for (int j = 0; j < dimension; j++)
                    {
                        embeddingValues[j] = (float)random.NextDouble();
                    }
                    var embedding = new Vector(embeddingValues);
                    insertCmd.Parameters.AddWithValue(embedding);
                    await insertCmd.ExecuteNonQueryAsync();
                }
            }
        }
    }

    /// <summary>
    /// Represents the result of an ANN search.
    /// </summary>
    public class SampleANNResult
    {
        /// <summary>
        /// The ID of the sample.
        /// </summary>
        public int Id { get; set; }
        /// <summary>
        /// The embedding vector of the sample.
        /// </summary>
        public Vector Embedding { get; set; } = default!;
    }

    /// <summary>
    /// Provides methods for performing Approximate Nearest Neighbors (ANN) searches on sample embeddings.
    /// </summary>
    public class ANNService {

        /// <summary>
        /// Performs an Approximate Nearest Neighbors (ANN) search on sample embeddings.
        /// </summary>
        /// <param name="conn">The PostgreSQL connection to use for the query.</param>
        /// <param name="dimension">The dimension of the embedding vectors.</param>
        /// <param name="nNeighbors">The number of nearest neighbors to retrieve.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a list of <see cref="SampleANNResult"/> objects representing the nearest neighbors.</returns>
        public async Task<List<SampleANNResult>> SampleANN(NpgsqlConnection conn, int dimension, int nNeighbors)
        {
            var results = new List<SampleANNResult>();
            
            await using (var cmd = new NpgsqlCommand("SELECT * FROM sample_embeddings_dotnet ORDER BY embedding <-> $1 LIMIT $2", conn))
            {
                // generate a random vector of the same dimensions to perform ANN search
                var random = new Random();
                var embeddingValues = new float[dimension];
                for (int j = 0; j < dimension; j++)
                {
                    embeddingValues[j] = (float)random.NextDouble();
                }
                var embedding = new Vector(embeddingValues);
                cmd.Parameters.AddWithValue(embedding);
                cmd.Parameters.AddWithValue(nNeighbors);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var result = new SampleANNResult
                        {
                            Id = reader.GetInt32(0),
                            Embedding = reader.GetFieldValue<Vector>(1)
                        };
                        results.Add(result);
                    }
                }
            }
            return results;
        }
    }
}
