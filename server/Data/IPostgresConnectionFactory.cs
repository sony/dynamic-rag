/// <summary>
/// Placeholder for the Postgres connection factory.
/// </summary>
public interface IPostgresConnectionFactory
{
    /// <summary>
    /// Creates a new Npgsql connection. (not used)
    /// </summary>
    Npgsql.NpgsqlConnection CreateConnection();
}