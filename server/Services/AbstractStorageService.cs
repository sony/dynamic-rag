using Microsoft.AspNetCore.Http;

namespace PgVectorDynamicRAG.Services
{
    /// <summary>
    /// Abstract base class for storage services that provides a common interface
    /// for different storage providers (Azure Blob Storage, AWS S3, etc.)
    /// </summary>
    public abstract class AbstractStorageService
    {
        protected readonly ILogger _logger;
        protected readonly string _containerName;

        /// <summary>
        /// Gets the container/bucket name
        /// </summary>
        public string ContainerName => _containerName;

        /// <summary>
        /// Constructor for the abstract storage service
        /// </summary>
        /// <param name="logger">Logger for the service</param>
        protected AbstractStorageService(ILogger logger)
        {
            _logger = logger;
            _containerName = GetContainerName();
            
            if (string.IsNullOrEmpty(_containerName))
            {
                throw new InvalidOperationException("Storage container name environment variable is not configured.");
            }
        }

        /// <summary>
        /// Gets the container/bucket name from environment variables.
        /// Each implementation should override this to use the appropriate environment variable.
        /// </summary>
        /// <returns>The container/bucket name</returns>
        protected abstract string GetContainerName();

        /// <summary>
        /// Reads all files from the storage container, organized by collection
        /// </summary>
        /// <returns>Dictionary containing files organized by collection</returns>
        public abstract Task<Dictionary<string, object>> ReadAllBlobs();

        /// <summary>
        /// Deletes all files for a specific collection
        /// </summary>
        /// <param name="collectionName">Name of the collection</param>
        /// <returns>Dictionary containing operation result</returns>
        public abstract Task<Dictionary<string, object>> DeleteCollectionBlobs(string collectionName);

        /// <summary>
        /// Deletes a single file by its relative path
        /// </summary>
        /// <param name="blobRelativePath">Relative path of the file to delete</param>
        /// <returns>Dictionary containing operation result</returns>
        public abstract Task<Dictionary<string, object>> DeleteSingleBlob(string blobRelativePath);

        /// <summary>
        /// Uploads a single file to the specified collection
        /// </summary>
        /// <param name="collectionName">Name of the collection</param>
        /// <param name="blobRelativePath">Relative path within the collection</param>
        /// <param name="file">File to upload</param>
        /// <returns>Dictionary containing upload details</returns>
        public abstract Task<Dictionary<string, object>> UploadSingleBlob(string collectionName, string blobRelativePath, IFormFile file);

        /// <summary>
        /// Downloads a single file and returns a URL for client access
        /// </summary>
        /// <param name="pathInContainer">Relative path of the file to download</param>
        /// <returns>Dictionary containing download details including access URL</returns>
        public abstract Task<Dictionary<string, object>> DownloadSingleBlob(string pathInContainer);

        /// <summary>
        /// Downloads a file directly to a stream for internal processing
        /// </summary>
        /// <param name="containerName">Container/bucket name</param>
        /// <param name="blobPath">Path of the file</param>
        /// <param name="targetStream">Stream to write the file data to</param>
        public abstract Task DownloadBlobToStreamAsync(string containerName, string blobPath, Stream targetStream);

        /// <summary>
        /// Saves a file stream to storage with the specified path
        /// </summary>
        /// <param name="blobPath">Path where the file should be stored</param>
        /// <param name="stream">Stream containing the file data</param>
        /// <param name="contentType">Content type of the file</param>
        /// <returns>Dictionary containing save operation details</returns>
        public abstract Task<Dictionary<string, object>> SaveFileStreamToStorage(string blobPath, Stream stream, string contentType);

        /// <summary>
        /// Checks if the configured container/bucket exists
        /// </summary>
        /// <returns>True if container exists, false otherwise</returns>
        protected abstract Task<bool> ContainerExistsAsync();
    }
}
