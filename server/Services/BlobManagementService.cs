using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace PgVectorDynamicRAG.Services
{
    /// 
    /// Service for managing blob storage operations in Azure.
    /// Provides methods for reading, uploading, and deleting blobs
    /// within a container organized by collection.
    /// 
    public class BlobManagementService : AbstractStorageService
    {
        public readonly BlobServiceClient _blobServiceClient;

        /// 
        /// Constructor for the blob management service.
        /// 
        /// <param name="logger">Logger for the service</param>
        public BlobManagementService(ILogger<BlobManagementService> logger) : base(logger)
        {
            // Get connection string from environment variables
            string connectionString = Environment.GetEnvironmentVariable("BLOB_STORAGE_CONNECTION_STRING");
            
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Blob storage connection string environment variable is not configured.");
            }
            
            // Initialize the blob service client
            _blobServiceClient = new BlobServiceClient(connectionString);
        }

        /// <summary>
        /// Gets the container name from environment variables for Azure Blob Storage
        /// </summary>
        /// <returns>The blob storage container name</returns>
        protected override string GetContainerName()
        {
            return Environment.GetEnvironmentVariable("BLOB_STORAGE_RAG_CONTAINER_NAME");
        }

    /// 
    /// Downloads a single blob and returns a SAS URL for client access
    /// 
    /// <param name="blobRelativePath">Relative path of the blob to download</param>
    /// <returns>Dictionary containing download details including SAS URL</returns>
    public override async Task<Dictionary<string, object>> DownloadSingleBlob(string pathInContainer)
    {
        try
        {
            _logger.LogInformation("Generating download URL for blob: {BlobPath}", pathInContainer);
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            
            // Check if container exists
            if (!await containerClient.ExistsAsync())
            {
                throw new InvalidOperationException($"Container '{_containerName}' does not exist.");
            }
            
            var blobClient = containerClient.GetBlobClient(pathInContainer);
            
            // Check if blob exists
            if (!await blobClient.ExistsAsync())
            {
                throw new InvalidOperationException($"Blob '{pathInContainer}' does not exist.");
            }
            
            // Generate a SAS token that expires in 1 hour
            var sasBuilder = new Azure.Storage.Sas.BlobSasBuilder
            {
                BlobContainerName = _containerName,
                BlobName = pathInContainer,
                Resource = "b", // b for blob
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(1)
            };
            
            // Set permissions for the SAS token
            sasBuilder.SetPermissions(Azure.Storage.Sas.BlobSasPermissions.Read);
            
            // Generate the SAS token
            // Extract the AccountKey from the connection string
            string connectionString = Environment.GetEnvironmentVariable("BLOB_STORAGE_CONNECTION_STRING");
            string accountKey = connectionString.Split(';')
                                                 .FirstOrDefault(part => part.StartsWith("AccountKey=", StringComparison.OrdinalIgnoreCase))
                                                 ?.Substring("AccountKey=".Length);
            if (string.IsNullOrEmpty(accountKey))
            {
                throw new InvalidOperationException("AccountKey is missing in the connection string.");
            }

            var sasToken = sasBuilder.ToSasQueryParameters(
                new Azure.Storage.StorageSharedKeyCredential(
                    _blobServiceClient.AccountName, 
                    accountKey
                )
            ).ToString();
            
            // Create the full SAS URL
            var sasUrl = $"{blobClient.Uri}?{sasToken}";
            
            // Get blob properties
            var properties = await blobClient.GetPropertiesAsync();
            
            return new Dictionary<string, object>
            {
                { "fileName", Path.GetFileName(pathInContainer) },
                { "contentType", properties.Value.ContentType },
                { "sizeMB", Math.Round(properties.Value.ContentLength / (1024.0 * 1024.0), 2) },
                { "downloadUrl", sasUrl },
                { "expiresOn", DateTimeOffset.UtcNow.AddHours(1) }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating download URL for blob: {BlobPath}", pathInContainer);
            throw;
        }
    }

        /// 
        /// Checks if the configured container exists
        /// 
        /// <returns>True if container exists, false otherwise</returns>
        protected override async Task<bool> ContainerExistsAsync()
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            return await containerClient.ExistsAsync();
        }

        /// 
        /// Reads all blobs from the container, organized by collection.
        /// 
        /// <returns>Dictionary containing blobs organized by collection</returns>
        public override async Task<Dictionary<string, object>> ReadAllBlobs()
        {
            try
            {
                _logger.LogInformation("Reading all blobs from container: {ContainerName}", _containerName);
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                _logger.LogInformation("Got the container client");
                // Check if container exists
                if (!await containerClient.ExistsAsync())
                {
                    throw new InvalidOperationException($"Container '{_containerName}' does not exist. Containers must be created through IaC.");
                }
                _logger.LogInformation("Created the container client");
                // Dictionary to store blobs by collection
                var blobsByCollection = new Dictionary<string, List<Dictionary<string, object>>>();
                
                // List all blobs in the container
                await foreach (BlobItem blobItem in containerClient.GetBlobsAsync())
                {
                    // Parse the blob path to get collection name
                    string blobPath = blobItem.Name;
                    string[] pathParts = blobPath.Split('/', 2);
                    
                    if (pathParts.Length < 2)
                    {
                        _logger.LogWarning("Blob {BlobPath} does not follow the expected path format: /collection/filename", blobPath);
                        continue;
                    }
                    
                    string collectionName = pathParts[0];
                    string relativePath = pathParts[1];
                    
                    // Create collection entry if it doesn't exist
                    if (!blobsByCollection.ContainsKey(collectionName))
                    {
                        blobsByCollection[collectionName] = new List<Dictionary<string, object>>();
                    }
                    
                    // Add blob info to the collection
                    var blobInfo = new Dictionary<string, object>
                    {
                        { "filename", Path.GetFileName(relativePath) },
                        { "pathInContainer", blobPath },
                        { "sizeMB", blobItem.Properties.ContentLength.HasValue ? Math.Round(blobItem.Properties.ContentLength.Value / (1024.0 * 1024.0), 2) : null }, // Size in MB
                        { "lastModified", blobItem.Properties.LastModified },
                        { "contentType", blobItem.Properties.ContentType }
                    };
                    
                    blobsByCollection[collectionName].Add(blobInfo);
                }
                
                return new Dictionary<string, object>
                {
                    { "collections", blobsByCollection }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading blobs from container: {ContainerName}", _containerName);
                throw;
            }
        }

        /// 
        /// Deletes all blobs for a specific collection.
        /// 
        /// <param name="collectionName">Name of the collection</param>
        /// <returns>Dictionary containing operation result</returns>
        public override async Task<Dictionary<string, object>> DeleteCollectionBlobs(string collectionName)
        {
            try
            {
                _logger.LogInformation("Deleting all blobs for collection: {CollectionName}", collectionName);
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                
                // Check if container exists
                if (!await containerClient.ExistsAsync())
                {
                    throw new InvalidOperationException($"Container '{_containerName}' does not exist. Containers must be created through IaC.");
                }
                
                int deletedCount = 0;
                
                // List all blobs with the collection prefix
                string prefix = $"{collectionName}/";
                await foreach (BlobItem blobItem in containerClient.GetBlobsAsync(prefix: prefix))
                {
                    var blobClient = containerClient.GetBlobClient(blobItem.Name);
                    await blobClient.DeleteIfExistsAsync();
                    deletedCount++;
                }
                
                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "deletedCount", deletedCount },
                    { "collectionName", collectionName }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting blobs for collection: {CollectionName}", collectionName);
                throw;
            }
        }

    /// 
    /// Deletes a single blob by its relative path.
    /// 
    /// <param name="blobRelativePath">Relative path of the blob to delete</param>
    /// <returns>Dictionary containing operation result</returns>
    public override async Task<Dictionary<string, object>> DeleteSingleBlob(string blobRelativePath)
    {
        try
        {
            _logger.LogInformation("Deleting blob: {BlobPath}", blobRelativePath);
            
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            
            // Check if container exists
            if (!await containerClient.ExistsAsync())
            {
                throw new InvalidOperationException($"Container '{_containerName}' does not exist. Containers must be created through IaC.");
            }
            
            // Sanitize the blob path to ensure consistent formatting
            string sanitizedBlobPath = blobRelativePath.StartsWith("/") ? blobRelativePath.Substring(1) : blobRelativePath;
            sanitizedBlobPath = sanitizedBlobPath.EndsWith("/") ? sanitizedBlobPath.Substring(0, sanitizedBlobPath.Length - 1) : sanitizedBlobPath;
            
            var blobClient = containerClient.GetBlobClient(sanitizedBlobPath);
            _logger.LogInformation("Attempting to delete blob at path: {BlobPath}", sanitizedBlobPath);
            
            // Check if blob exists before attempting to delete
            bool exists = await blobClient.ExistsAsync();
            bool deleted = false;
            
            if (exists)
            {
                var response = await blobClient.DeleteAsync();
                deleted = true;
                _logger.LogInformation("Blob successfully deleted: {BlobPath}", sanitizedBlobPath);
            }
            else
            {
                _logger.LogWarning("Blob not found for deletion: {BlobPath}", sanitizedBlobPath);
            }
            
            return new Dictionary<string, object>
            {
                { "deleted", deleted },
                { "pathInContainer", sanitizedBlobPath },
                { "blobExists", exists },
                { "containerName", _containerName }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting blob: {BlobPath}", blobRelativePath);
            throw;
        }
    }

        /// 
        /// Uploads a single blob to the specified collection.
        /// 
        /// <param name="collectionName">Name of the collection</param>
        /// <param name="blobRelativePath">Relative path within the collection</param>
        /// <param name="file">File to upload</param>
        /// <returns>Dictionary containing upload details</returns>
        public override async Task<Dictionary<string, object>> UploadSingleBlob(string collectionName, string blobRelativePath, IFormFile file)
        {
            try
            {
                _logger.LogInformation("Uploading blob to collection: {CollectionName}, path: {BlobPath}", collectionName, blobRelativePath);
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                
                // Check if container exists
                if (!await containerClient.ExistsAsync())
                {
                    throw new InvalidOperationException($"Container '{_containerName}' does not exist. Containers must be created through IaC.");
                }
                
                // Construct the full blob path
                string sanitizedBlobRelativePath = blobRelativePath.StartsWith("/") ? blobRelativePath.Substring(1) : blobRelativePath;
                sanitizedBlobRelativePath = sanitizedBlobRelativePath.EndsWith("/") ? sanitizedBlobRelativePath.Substring(0, sanitizedBlobRelativePath.Length - 1) : sanitizedBlobRelativePath;
                string sanitizedFileName = file.FileName.StartsWith("/") ? file.FileName.Substring(1) : file.FileName;
                sanitizedFileName = sanitizedFileName.EndsWith("/") ? sanitizedFileName.Substring(0, sanitizedFileName.Length - 1) : sanitizedFileName;
                string fullBlobPath = $"{collectionName}/{sanitizedBlobRelativePath}/{sanitizedFileName}";
                // Get a reference to the blob
                var blobClient = containerClient.GetBlobClient(fullBlobPath);
                
                // Set blob HTTP headers (content type)
                var blobHttpHeaders = new BlobHttpHeaders
                {
                    ContentType = file.ContentType
                };
                
                // Upload the file
                using (var stream = file.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, new BlobUploadOptions { HttpHeaders = blobHttpHeaders });
                }
                
                return new Dictionary<string, object>
                {
                    { "fileName", file.FileName },
                    { "contentType", file.ContentType },
                    { "sizeMB", Math.Round(file.Length / (1024.0 * 1024.0), 2) }, // Size in MB
                    { "collectionName", collectionName },
                    { "blobPath", fullBlobPath },
                    { "url", blobClient.Uri.ToString() }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading blob to collection: {CollectionName}, path: {BlobPath}", collectionName, blobRelativePath);
                throw;
            }
        }

        public override async Task DownloadBlobToStreamAsync(string containerName, string blobPath, Stream targetStream)
        {
            BlobContainerClient containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            BlobClient blobClient = containerClient.GetBlobClient(blobPath);
            
            await blobClient.DownloadToAsync(targetStream);
            targetStream.Position = 0; // Reset position to beginning of stream
        }

        /// <summary>
        /// Saves a file stream to blob storage with the specified path
        /// </summary>
        /// <param name="blobPath">Path where the file should be stored</param>
        /// <param name="stream">Stream containing the file data</param>
        /// <param name="contentType">Content type of the file</param>
        /// <returns>Dictionary containing save operation details</returns>
        public override async Task<Dictionary<string, object>> SaveFileStreamToStorage(string blobPath, Stream stream, string contentType)
        {
            try
            {
                _logger.LogInformation("Saving file stream to blob storage: {BlobPath}", blobPath);
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                
                // Check if container exists
                if (!await containerClient.ExistsAsync())
                {
                    throw new InvalidOperationException($"Container '{_containerName}' does not exist. Containers must be created through IaC.");
                }
                
                // Get a reference to the blob
                var blobClient = containerClient.GetBlobClient(blobPath);
                
                // Set blob HTTP headers (content type)
                var blobHttpHeaders = new BlobHttpHeaders
                {
                    ContentType = contentType
                };
                
                // Upload the stream
                await blobClient.UploadAsync(stream, new BlobUploadOptions { HttpHeaders = blobHttpHeaders });
                
                return new Dictionary<string, object>
                {
                    { "blobPath", blobPath },
                    { "contentType", contentType },
                    { "url", blobClient.Uri.ToString() },
                    { "success", true }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving file stream to blob storage: {BlobPath}", blobPath);
                throw;
            }
        }
    }
}
