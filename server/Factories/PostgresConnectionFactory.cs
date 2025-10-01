using Npgsql;

/// <summary>
/// Factory class for creating PostgreSQL database connections using Npgsql.
/// </summary>
public class PostgresConnectionFactory : IPostgresConnectionFactory
{
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresConnectionFactory"/> class.
    /// </summary>
    /// <param name="configuration">The application configuration used to retrieve connection 
    public PostgresConnectionFactory(IConfiguration configuration)
    {
        var host = Environment.GetEnvironmentVariable("PG_HOST");
        var database = Environment.GetEnvironmentVariable("PG_DATABASE");
        var username = Environment.GetEnvironmentVariable("PG_USER");
        var password = Environment.GetEnvironmentVariable("PG_PASSWORD");
        var connectionString = $"Host={host};Database={database};Username={username};Password={password}";
        // var connectionString = configuration.GetConnectionString("PostgresConnection");

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        _dataSource = dataSourceBuilder.Build();
    }

    /// <summary>
    /// Creates and returns a new instance of <see cref="NpgsqlConnection"/>.
    /// </summary>
    /// <returns>A new <see cref="NpgsqlConnection"/> instance.</returns>
    public NpgsqlConnection CreateConnection()
    {
        // Create and return a new connection instance each time.
        return _dataSource.CreateConnection();
    }
}