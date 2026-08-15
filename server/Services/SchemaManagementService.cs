using Npgsql;
using ModelContextProtocol.Server;
using System.ComponentModel;
using PgVectorDynamicRAG.Data;

namespace PgVectorDynamicRAG.Services
{
    /// <summary>
    /// The SchemaManagementService defines the methods used by the SchemaManagementController.
    /// These methods define how the RAG system tracks various collections and models for quick lookups.
    /// </summary>
    [McpServerToolType]
    public class SchemaManagementService
    {
        private readonly ILogger<SchemaManagementService> _logger;
        private readonly IPostgresConnectionFactory _connectionFactory;
        /// <summary>
        /// Constructor for the schema management service
        /// </summary>
        /// <param name="kernel"></param>
        /// <param name="logger"></param>
        /// <param name="connectionFactory"></param>
        public SchemaManagementService(ILogger<SchemaManagementService> logger,
                                        IPostgresConnectionFactory connectionFactory)
        {
            _logger = logger;
            _connectionFactory = connectionFactory;
        }

        /// <summary>
        /// Check whether or not the fileName already has at least one item in a named collection
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="collectionName"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<bool> CheckFileExistsInCollection(string fileName, string collectionName, CancellationToken cancellationToken = default)
        {
            SqlIdentifierValidator.Validate(collectionName, nameof(collectionName));
            var sql = $@"
                SELECT EXISTS(
                    SELECT 1
                    FROM ""{collectionName}""
                    WHERE ""FileName"" = @fileName
                );
            ";
            await using var conn = _connectionFactory.CreateConnection();
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("fileName", fileName);

            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is bool exists && exists;
        }

        /// <summary>
        /// Validate the embedding model by checking if it exists in the database.
        /// </summary>
        /// <param name="embeddingModelName"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<(string, int)> ValidateEmbeddingModel(string embeddingModelName, CancellationToken cancellationToken = default)
        {
            List<string> service_ids = new List<string>();
            List<int> dimensions = new List<int>();
            try
            {
                var validateNameQuery = @"
                    SELECT service_id, dimension
                    FROM embedding_model_directory
                    WHERE deployment_name = @modelName
                    ORDER BY model_version DESC
                ";

                await using (var conn = _connectionFactory.CreateConnection())
                {
                    await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                    await using (var cmd = new NpgsqlCommand(validateNameQuery, conn))
                    {
                        // Add the parameter for model_name
                        cmd.Parameters.AddWithValue("modelName", embeddingModelName);

                        // ExecuteReaderAsync to read the results
                        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            {
                                // service_ids.Add(reader.GetInt32(0));
                                service_ids.Add(reader.GetString(0));
                                dimensions.Add(reader.GetInt32(1));
                            }
                        }
                    }
                }

                if (service_ids.Count == 0)
                {
                    throw new Exception("This model is not registered.");
                }

                // Set modelId to the first value in the list (latest model version)
                string serviceId = service_ids.First();
                int dimension = dimensions.First();
                // return serviceId;
                return (serviceId, dimension);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while validating the embedding model.");
                throw;
            }
        }

        /// <summary>
        /// Initialize the core schema for the RAG system.
        /// </summary>
        /// <param name="postgresConnectionString"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static async Task<bool> InitializeCoreSchema(
            string postgresConnectionString,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // SQL to create the vector extension if it's available
                const string createExtensionSql = "CREATE EXTENSION IF NOT EXISTS vector;";

                // Table creation SQL statements
                const string createColTabSql = @"
                    CREATE TABLE IF NOT EXISTS collection_directory (
                        collection_name text primary key,
                        collection_type TEXT NOT NULL,
                        default_embedding_service_id TEXT NOT NULL,
                        default_distance_metric TEXT NOT NULL,
                        description text
                    );";

                const string createEmbTabSql = @"
                    CREATE TABLE IF NOT EXISTS embedding_model_directory (
                        model_id                    SERIAL PRIMARY KEY,
                        deployment_name             text NOT NULL,
                        model_version               text NOT NULL,
                        modality                    text NOT NULL,
                        dimension                   INT NOT NULL,
                        created_on                  timestamp DEFAULT NOW(),
                        service_id                  TEXT GENERATED ALWAYS AS (deployment_name || '_' || model_version) STORED,
                        currently_available         boolean NOT NULL DEFAULT false
                    );";

                const string createChatTabSql = @"
                    CREATE TABLE IF NOT EXISTS chat_model_directory (
                        model_id                    SERIAL PRIMARY KEY,
                        deployment_name             text NOT NULL,
                        model_version               text NOT NULL,
                        created_on                  timestamp DEFAULT NOW(),
                        service_id                  TEXT GENERATED ALWAYS AS (deployment_name || '_' || model_version) STORED,
                        currently_available         boolean NOT NULL DEFAULT false
                    );";

                // Open a single connection for all initialization steps
                await using (var conn = new NpgsqlConnection(postgresConnectionString))
                {
                    await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

                    // 1️⃣ Attempt to install the vector extension; ignore any errors
                    await using (var cmdExt = new NpgsqlCommand(createExtensionSql, conn))
                    {
                        await cmdExt.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }

                    // 2️⃣ Create core tables
                    await using (var cmd1 = new NpgsqlCommand(createColTabSql, conn))
                    {
                        await cmd1.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                    await using (var cmd2 = new NpgsqlCommand(createEmbTabSql, conn))
                    {
                        await cmd2.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                    await using (var cmd3 = new NpgsqlCommand(createChatTabSql, conn))
                    {
                        await cmd3.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                }

                return true;
            }
            catch
            {
                // _logger.LogError(ex, "An error occurred while initializing the collection manager.");
                return false;
            }
        }

        /// <summary>
        /// Delete a collection from the collection_directory table.
        /// </summary>
        /// <param name="collectionName"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<Boolean> DeleteCollectionFromManager(string collectionName,
                                                                CancellationToken cancellationToken = default)
        {
            var sql = @"
                DELETE FROM collection_directory
                WHERE collection_name = @collectionName;
            ";

            int rowsAffected;

            await using (var conn = _connectionFactory.CreateConnection())
            {
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    // Add the parameter for collection_name.
                    cmd.Parameters.AddWithValue("collectionName", collectionName);

                    // ExecuteNonQueryAsync returns the number of rows affected.
                    rowsAffected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    
                    // Optionally, you can log or act on rowsAffected to determine if the deletion was successful.
                    _logger.LogInformation("{RowsAffected} row(s) were deleted from collection_directory.", rowsAffected);
                }
            }

            if ( rowsAffected > 0 )
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Delete a collection from the schema.
        /// </summary>
        /// <param name="collectionName"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<bool> DeleteCollectionFromSchema(string collectionName, CancellationToken cancellationToken = default)
        {
            SqlIdentifierValidator.Validate(collectionName, nameof(collectionName));
            var sql = $@"DROP TABLE IF EXISTS ""{collectionName}"";";

            try
            {
                await using (var conn = _connectionFactory.CreateConnection())
                {
                    await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                    await using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        // ExecuteNonQueryAsync executes the command. Note that DROP TABLE does not return
                        // a row count in a meaningful way (often -1), so we consider the command successful
                        // if it executes without throwing an exception.
                        int result = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                _logger.LogInformation("Dropped table {CollectionName} from the scehema", LogSanitizer.Clean(collectionName));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to drop table {CollectionName} from schema.", LogSanitizer.Clean(collectionName));
                return false;
            }
        }

        /// <summary>
        /// Check if a collection exists in the collection_directory table.
        /// </summary>
        /// <param name="collectionName"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<Boolean> CheckCollectionExists(string collectionName,
                                                    CancellationToken cancellationToken = default)
        {
            // Check to make sure that collection exists, and if so, get the embedding model that this collection uses.
            var sql = @"
                SELECT EXISTS(
                    SELECT 1
                    FROM collection_directory
                    WHERE collection_name = @collectionName
                );
            ";

            bool collectionExists;

            await using (var conn = _connectionFactory.CreateConnection())
            {
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    // Add the parameter for collection_name
                    cmd.Parameters.AddWithValue("collectionName", collectionName);

                    // ExecuteScalarAsync returns the first column of the first row in the result set.
                    collectionExists = (bool)await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            if (collectionExists)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

    /// <summary>
    /// Get the default embedding service id for a given collection.
    /// </summary>
    /// <param name="collectionName"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    /// <exception cref="ArgumentException"></exception>
    public async Task<(string, string)> getDefaultEmbeddingServiceDetails(
        string collectionName,
        CancellationToken cancellationToken = default)
    {

        // SQL query to select the primary_embedding_service_id for the given collectionName.
        var service_id_from_collection_name_sql = @"
            SELECT default_embedding_service_id, collection_type
            FROM collection_directory
            WHERE collection_name = @collectionName;
        ";

        string embeddingServiceId;
        string embeddingServiceModality;

        await using (var conn = _connectionFactory.CreateConnection())
            {
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(collectionName))
                {
                    await using (var cmd = new NpgsqlCommand(service_id_from_collection_name_sql, conn))
                    {
                        // Add the parameter for collection_name.
                        cmd.Parameters.AddWithValue("@collectionName", collectionName);

                        // Execute the query.
                        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                        {
                            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            {
                                embeddingServiceId = reader.GetString(reader.GetOrdinal("default_embedding_service_id"));
                                embeddingServiceModality = reader.GetString(reader.GetOrdinal("collection_type"));
                            }
                            else
                            {
                                throw new Exception($"Collection {collectionName} does not exist.");
                            }
                        }
                    }
                }
                else
                {
                    throw new ArgumentException("A valid collectionName must be provided.");
                }
            }

        return (embeddingServiceId, embeddingServiceModality);
    }

    /// <summary>
    /// Get the default distance metric for a given collection.
    /// </summary>
    /// <param name="embeddingServiceId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public async Task<string> getEmbeddingServiceName(
        string embeddingServiceId,
        CancellationToken cancellationToken = default)
    {
        // SQL query to select the embedding_service_name for the given embeddingServiceId.
        var sql = @"
        SELECT deployment_name
        FROM embedding_model_directory
        WHERE service_id = @embeddingServiceId;
    ";

        string embeddingServiceName;

        await using (var conn = _connectionFactory.CreateConnection())
        {
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (var cmd = new NpgsqlCommand(sql, conn))
            {
                // Add the parameter for service_id.
                cmd.Parameters.AddWithValue("embeddingServiceId", embeddingServiceId);

                // Execute the query.
                await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        embeddingServiceName = reader.GetString(reader.GetOrdinal("deployment_name"));
                    }
                    else
                    {
                        throw new Exception($"Embedding service with id {embeddingServiceId} does not exist.");
                    }
                }
            }
        }

        return embeddingServiceName;
    }

    /// <summary>
    /// Gets the chat service ID for a given deployment name.
    /// </summary>
    /// <param name="deploymentName">The deployment name (e.g., "gpt-4o")</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The service ID (e.g., "gpt-4o_2024-02-15-preview")</returns>
    public async Task<string> getChatServiceId(
        string deploymentName,
        CancellationToken cancellationToken = default)
    {
        // SQL query to select the service_id for the given deployment name from available chat models
        var sql = @"
        SELECT service_id
        FROM chat_model_directory
        WHERE deployment_name = @deploymentName 
        AND currently_available = TRUE
        LIMIT 1;
    ";

        string serviceId;

        await using (var conn = _connectionFactory.CreateConnection())
        {
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (var cmd = new NpgsqlCommand(sql, conn))
            {
                // Add the parameter for deployment_name.
                cmd.Parameters.AddWithValue("deploymentName", deploymentName);

                // Execute the query.
                await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        serviceId = reader.GetString(reader.GetOrdinal("service_id"));
                    }
                    else
                    {
                        throw new Exception($"Chat service with deployment name '{deploymentName}' does not exist or is not currently available.");
                    }
                }
            }
        }

        return serviceId;
    }

    /// <summary>
    /// Checks whether a service exists in the database.
    /// </summary>
    /// <param name="conn"></param>
    /// <param name="query"></param>
    /// <param name="service"></param>
    /// <param name="addDetailsMethod"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private static async Task<string?> ServiceExistsAsync(NpgsqlConnection conn,
                                                        string query,
                                                        Dictionary<string, object> service,
                                                        Action<NpgsqlCommand, Dictionary<string, object>> addDetailsMethod,
                                                        CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(query, conn);
        addDetailsMethod(cmd, service);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return reader.GetString(0); // Assuming service_id is the first column in the result set
        }
        return null;
    }

    /// <summary>
    /// Inserts a model into the database.
    /// </summary>
    /// <param name="conn"></param>
    /// <param name="query"></param>
    /// <param name="service"></param>
    /// <param name="addDetailsMethod"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private static async Task<string> InsertModelAsync(NpgsqlConnection conn,
                                            string query,
                                            Dictionary<string, object> service,
                                            Action<NpgsqlCommand, Dictionary<string, object>> addDetailsMethod,
                                            CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(query, conn);
        cmd.Parameters.AddWithValue("createdOn", DateTime.UtcNow);
        addDetailsMethod(cmd, service);

        return (string)await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false); // Returning the model_id
    }

    // /// <summary>
    // /// Validates or adds models to the database.
    // /// </summary>
    // /// <param name="serviceList"></param>
    // /// <param name="modelType"></param>
    // /// <param name="postgresConnectionString"></param>
    // /// <param name="cancellationToken"></param>
    // /// <returns></returns>
    // /// <exception cref="NotImplementedException"></exception>
    // public static async Task<List<Dictionary<string, object>>> ValidateOrAddModels(
    //         List<Dictionary<string, object>> serviceList,
    //         string modelType,
    //         string postgresConnectionString,
    //         CancellationToken cancellationToken = default)
    // {
    //     string tableName;
    //     using (var conn = new NpgsqlConnection(postgresConnectionString))
    //     {
    //         await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
    //         string serviceExistsQuery;
    //         string insertModelQuery;
    //         Action<NpgsqlCommand, Dictionary<string, object>> addDetailsMethod;

    //         if (modelType == "Embedding")
    //         {
    //             tableName = "embedding_model_directory";
    //             serviceExistsQuery = $"SELECT service_id FROM {tableName} WHERE deployment_name = @deploymentName AND model_version = @modelVersion AND modality = @modality AND dimension = @dimension";
    //             insertModelQuery = $"INSERT INTO {tableName} (deployment_name, model_version, modality, dimension, created_on) VALUES (@deploymentName, @modelVersion, @modality, @dimension, @createdOn) RETURNING service_id";
    //             addDetailsMethod = (cmd, service) => {
    //                 cmd.Parameters.AddWithValue("deploymentName", NpgsqlTypes.NpgsqlDbType.Text, service["DeploymentName"]);
    //                 cmd.Parameters.AddWithValue("modelVersion", NpgsqlTypes.NpgsqlDbType.Text, service["ModelVersion"]);
    //                 cmd.Parameters.AddWithValue("modality", NpgsqlTypes.NpgsqlDbType.Text, service["Modality"]);
    //                 cmd.Parameters.AddWithValue("dimension", NpgsqlTypes.NpgsqlDbType.Integer, Convert.ToInt32(service["Dimension"]));
    //             };
    //         }
    //         else if (modelType == "ChatCompletion")
    //         {
    //             tableName = "chat_model_directory";
    //             serviceExistsQuery = $"SELECT service_id FROM {tableName} WHERE deployment_name = @deploymentName AND model_version = @modelVersion";
    //             insertModelQuery = $"INSERT INTO {tableName} (deployment_name, model_version, created_on) VALUES (@deploymentName, @modelVersion, @createdOn) RETURNING service_id";
    //             addDetailsMethod = (cmd, service) => {
    //                 cmd.Parameters.AddWithValue("deploymentName", NpgsqlTypes.NpgsqlDbType.Text, service["DeploymentName"]);
    //                 cmd.Parameters.AddWithValue("modelVersion", NpgsqlTypes.NpgsqlDbType.Text, service["ModelVersion"]);
    //             };
    //         }
    //         else
    //         {
    //             throw new NotImplementedException($"Validating models of type {modelType} is not supported");
    //         }

    //         List<Dictionary<string, object>> updatedServiceList = new List<Dictionary<string, object>>();

    //         foreach (var service in serviceList)
    //         {
    //             Guid? serviceId = await ServiceExistsAsync(conn, serviceExistsQuery, service, addDetailsMethod, cancellationToken);

    //             if (serviceId == null)
    //             {
    //                 serviceId = await InsertModelAsync(conn, insertModelQuery, service, addDetailsMethod, cancellationToken);
    //             }

    //             service["ServiceId"] = serviceId.ToString();
    //             updatedServiceList.Add(service);
    //         }

    //         return updatedServiceList;
    //     }
    // }

    /// <summary>
    /// Validates or adds models to the database, ensuring that models present in the provided serviceList
    /// have their currently_available flag set to TRUE, and that all other models have currently_available set to FALSE.
    /// </summary>
    /// <param name="serviceList"></param>
    /// <param name="modelType"></param>
    /// <param name="postgresConnectionString"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    /// Warning: this method does not return the model with the highest available quota.
    public static async Task<List<Dictionary<string, object>>> ValidateOrAddModels(
            List<Dictionary<string, object>> serviceList,
            string modelType,
            string postgresConnectionString,
            CancellationToken cancellationToken = default)
    {
        string tableName;
        using (var conn = new NpgsqlConnection(postgresConnectionString))
        {
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            string serviceExistsQuery;
            string insertModelQuery;
            Action<NpgsqlCommand, Dictionary<string, object>> addDetailsMethod;

            if (modelType == "TextEmbedding" || modelType == "ImageEmbedding")
            {
                tableName = "embedding_model_directory";
                // Look up a record regardless of its current availability.
                serviceExistsQuery = $@"
                    SELECT service_id 
                    FROM {tableName} 
                    WHERE deployment_name = @deploymentName 
                    AND model_version = @modelVersion 
                    AND modality = @modality 
                    AND dimension = @dimension";
                // When inserting a new record, explicitly set currently_available to TRUE.
                insertModelQuery = $@"
                    INSERT INTO {tableName} 
                        (deployment_name, model_version, modality, dimension, created_on, currently_available) 
                    VALUES 
                        (@deploymentName, @modelVersion, @modality, @dimension, @createdOn, TRUE)
                    RETURNING service_id";
                addDetailsMethod = (cmd, service) =>
                {
                    cmd.Parameters.AddWithValue("deploymentName", NpgsqlTypes.NpgsqlDbType.Text, service["DeploymentName"]);
                    cmd.Parameters.AddWithValue("modelVersion", NpgsqlTypes.NpgsqlDbType.Text, service["ModelVersion"]);
                    cmd.Parameters.AddWithValue("modality", NpgsqlTypes.NpgsqlDbType.Text, service["Modality"]);
                    cmd.Parameters.AddWithValue("dimension", NpgsqlTypes.NpgsqlDbType.Integer, Convert.ToInt32(service["Dimension"]));
                };
            }
            else if (modelType == "ChatCompletion")
            {
                tableName = "chat_model_directory";
                // Look up a record regardless of its current availability.
                serviceExistsQuery = $@"
                    SELECT service_id 
                    FROM {tableName} 
                    WHERE deployment_name = @deploymentName 
                    AND model_version = @modelVersion";
                // Insert new chat model records with currently_available set to TRUE.
                insertModelQuery = $@"
                    INSERT INTO {tableName} 
                        (deployment_name, model_version, created_on, currently_available) 
                    VALUES 
                        (@deploymentName, @modelVersion, @createdOn, TRUE)
                    RETURNING service_id";
                addDetailsMethod = (cmd, service) =>
                {
                    cmd.Parameters.AddWithValue("deploymentName", NpgsqlTypes.NpgsqlDbType.Text, service["DeploymentName"]);
                    cmd.Parameters.AddWithValue("modelVersion", NpgsqlTypes.NpgsqlDbType.Text, service["ModelVersion"]);
                };
            }
            else
            {
                throw new NotImplementedException($"Validating models of type {modelType} is not supported");
            }

            List<Dictionary<string, object>> updatedServiceList = new List<Dictionary<string, object>>();

            // Process each service in the list.
            foreach (var service in serviceList)
            {
                // Check existence ignoring currently_available.
                string? serviceId = await ServiceExistsAsync(conn, serviceExistsQuery, service, addDetailsMethod, cancellationToken);

                if (serviceId == null)
                {
                    // Insert new model; insertModelQuery already sets currently_available to TRUE.
                    serviceId = await InsertModelAsync(conn, insertModelQuery, service, addDetailsMethod, cancellationToken);
                }
                else
                {
                    // Record exists—update its currently_available flag to TRUE in case it was previously false.
                    string updateAvailabilityQuery = $"UPDATE {tableName} SET currently_available = TRUE WHERE service_id = @serviceId";
                    using (var updateCmd = new NpgsqlCommand(updateAvailabilityQuery, conn))
                    {
                        updateCmd.Parameters.AddWithValue("serviceId", NpgsqlTypes.NpgsqlDbType.Text, serviceId);
                        await updateCmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                service["ServiceId"] = serviceId;
                updatedServiceList.Add(service);
            }

            // Now update the table so that any model not in the current serviceList is marked unavailable.
            // We build composite conditions based on the unique keys for each model type.
            if (serviceList.Count > 0)
            {
                List<string> conditions = new List<string>();
                var parameters = new List<NpgsqlParameter>();
                int index = 0;
                if (modelType == "TextEmbedding" || modelType == "ImageEmbedding")
                {
                    foreach (var service in serviceList)
                    {
                        conditions.Add($"(deployment_name = @dep_{index} AND model_version = @modVer_{index} AND modality = @modality_{index} AND dimension = @dim_{index})");
                        parameters.Add(new NpgsqlParameter($"dep_{index}", NpgsqlTypes.NpgsqlDbType.Text) { Value = service["DeploymentName"] });
                        parameters.Add(new NpgsqlParameter($"modVer_{index}", NpgsqlTypes.NpgsqlDbType.Text) { Value = service["ModelVersion"] });
                        parameters.Add(new NpgsqlParameter($"modality_{index}", NpgsqlTypes.NpgsqlDbType.Text) { Value = service["Modality"] });
                        parameters.Add(new NpgsqlParameter($"dim_{index}", NpgsqlTypes.NpgsqlDbType.Integer) { Value = Convert.ToInt32(service["Dimension"]) });
                        index++;
                    }
                }
                else if (modelType == "ChatCompletion")
                {
                    foreach (var service in serviceList)
                    {
                        conditions.Add($"(deployment_name = @dep_{index} AND model_version = @modVer_{index})");
                        parameters.Add(new NpgsqlParameter($"dep_{index}", NpgsqlTypes.NpgsqlDbType.Text) { Value = service["DeploymentName"] });
                        parameters.Add(new NpgsqlParameter($"modVer_{index}", NpgsqlTypes.NpgsqlDbType.Text) { Value = service["ModelVersion"] });
                        index++;
                    }
                }
                // Combine all individual conditions with OR.
                string compositeCondition = string.Join(" OR ", conditions);
                
                // Update any record that does not match any of the provided conditions.
                // Add a modality filter when processing specific embedding types
                string modalityFilter = "";
                if (modelType == "ImageEmbedding")
                {
                    modalityFilter = " AND modality = 'ImageEmbedding'";
                }
                else if (modelType == "TextEmbedding")
                {
                    modalityFilter = " AND modality = 'TextEmbedding'";
                }
                
                string updateNonAvailableQuery = $"UPDATE {tableName} SET currently_available = FALSE WHERE NOT ({compositeCondition}){modalityFilter}";
                using (var updateNonCmd = new NpgsqlCommand(updateNonAvailableQuery, conn))
                {
                    updateNonCmd.Parameters.AddRange(parameters.ToArray());
                    await updateNonCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            else
            {
                // If the service list is empty, mark all rows as unavailable.
                string updateNonAvailableQuery = $"UPDATE {tableName} SET currently_available = FALSE WHERE modality = @modelType";
                using (var updateNonCmd = new NpgsqlCommand(updateNonAvailableQuery, conn))
                {
                    updateNonCmd.Parameters.AddWithValue("modelType", modelType);
                    await updateNonCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            return updatedServiceList;
        }
    }

    /// <summary>
    /// Read all chat models that have ben registered within the chat_model_directory with
    /// a unique service_id. 
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [McpServerTool, Description("Read all chat models that have been registered within the chat_model_directory with a unique service_id.")]
    public async Task<List<string>> ReadChatModels(CancellationToken cancellationToken = default)
    {
        List<string> chatModels = new List<string>();
        string sql = @"
            SELECT DISTINCT deployment_name
            FROM chat_model_directory
            WHERE currently_available
            ORDER BY deployment_name ASC;
        ";

        await using (var conn = _connectionFactory.CreateConnection())
            {
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            chatModels.Add(reader.GetString(0));
                        }
                    }
                }
            }

        return chatModels;
    }

    /// <summary>
    /// Read the most recent unique embedding models from the embedding_model_directory table,
    /// grouped by modality.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary with modality as key and list of deployment names as values</returns>
    [McpServerTool, Description("Read the most recent unique embedding models from the embedding_model_directory table, grouped by modality.")]
    public async Task<Dictionary<string, List<string>>> ReadEmbeddingModels(CancellationToken cancellationToken = default)
    {
        Dictionary<string, List<string>> embeddingModels = new Dictionary<string, List<string>>();
        string sql = @"
            SELECT DISTINCT deployment_name, modality
            FROM embedding_model_directory
            WHERE currently_available
            ORDER BY deployment_name ASC;
        ";

        await using (var conn = _connectionFactory.CreateConnection())
        {
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            using (var cmd = new NpgsqlCommand(sql, conn))
            {
                await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        string deploymentName = reader.GetString(0);
                        string modality = reader.GetString(1);
                        
                        // If this modality isn't in the dictionary yet, add it with an empty list
                        if (!embeddingModels.ContainsKey(modality))
                        {
                            embeddingModels[modality] = new List<string>();
                        }
                        
                        // Add the deployment name to the list for this modality
                        embeddingModels[modality].Add(deploymentName);
                    }
                }
            }
        }

        return embeddingModels;
    }

    /// <summary>
    /// Read the list of available collections that are in the RAG database.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns>List of collection details including name, embedding service ID, and distance metric</returns>
    [McpServerTool, Description("Read the list of available collections that are in the RAG database.")]
    public async Task<List<Dictionary<string, string>>> ReadCollections(CancellationToken cancellationToken = default)
    {
        List<Dictionary<string, string>> collections = new List<Dictionary<string, string>>();
        string sql = @"
            SELECT collection_name, collection_type, default_embedding_service_id, default_distance_metric, description
            FROM collection_directory
            ORDER BY collection_name ASC;
        ";

        await using (var conn = _connectionFactory.CreateConnection())
        {
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            using (var cmd = new NpgsqlCommand(sql, conn))
            {
                await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var collectionInfo = new Dictionary<string, string>
                        {
                            { "collection_name", reader.GetString(0) },
                            { "collection_type", reader.GetString(1) },
                            { "default_embedding_service_id", reader.GetString(2) },
                            { "default_distance_metric", reader.GetString(3) },
                            { "description", reader.IsDBNull(4) ? "" : reader.GetString(4) } 
                        };
                        collections.Add(collectionInfo);
                    }
                }
            }
        }
        return collections;
    }
    }
}