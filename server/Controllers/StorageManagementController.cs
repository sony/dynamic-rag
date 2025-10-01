using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PgVectorDynamicRAG.Services;
using PgVectorDynamicRAG.Data;

namespace PgVectorDynamicRAG.Controllers
{
    /// <summary>
    /// The StorageManagementController uses an AbstractStorageService to provide
    /// a real-time API interface to the dedicated storage container/bucket,
    /// and all files and directories within it.
    /// This controller serves to support file uploads from storage providers,
    /// keeping files within the RAG database up to date with the storage
    /// account whenever requests come through this API
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class StorageManagementController : ControllerBase
    {
        private readonly AbstractStorageService _storageManagementService;
        /// <summary>
        /// Constructor for the storage management controller object
        /// </summary>
        public StorageManagementController(AbstractStorageService storageManagementService)
        {
            _storageManagementService = storageManagementService;
        }

        /// <summary>
        /// Route to the /read-all-blobs GET endpoint
        /// </summary>
        [HttpGet("read-all-blobs")]
        public async Task<IActionResult> ReadAllBlobs()
        {
            try
            {
                var startTime = DateTime.UtcNow;

                Dictionary<string, object> blobsByContainer = await _storageManagementService.ReadAllBlobs();
                var endTime = DateTime.UtcNow;

                blobsByContainer["internal_execution_time"] = (endTime - startTime).TotalSeconds;
                // blobsByContainer["blobs_by_container"] = result.collections;

                return Ok(blobsByContainer);
            }
            catch (Exception ex)
            {
                return Problem($"Failed to read all blobs for all collections: {ex.Message}");
            }
        }

        /// <summary>
        /// Route to the /delete-collection-blobs POST endpoint
        /// </summary>
        [HttpPost("delete-collection-blobs")]
        public async Task<IActionResult> DeleteCollectionBlobs([FromBody] DeleteCollectionBlobsRequest request)
        {
            try
            {
                var startTime = DateTime.UtcNow;

                Dictionary<string, object> result = await _storageManagementService.DeleteCollectionBlobs(request.collectionName);
                
                var endTime = DateTime.UtcNow;

                result["internal_execution_time"] = (endTime - startTime).TotalSeconds;
                result["status"] = "success";

                return Ok(result);
            }
            catch (Exception ex)
            {
                return Problem($"Failed to read all blobs for all collections: {ex.Message}");
            }
        }

        /// <summary>
        /// Route to the /delete-single-blob POST endpoint
        /// </summary>
        [HttpPost("delete-single-blob")]
        public async Task<IActionResult> DeleteSingleBlob([FromBody] SingleBlobRequest request)
        {
            try
            {
                var startTime = DateTime.UtcNow;

                Dictionary<string, object> result = await _storageManagementService.DeleteSingleBlob(request.pathInContainer);
                
                var endTime = DateTime.UtcNow;

                result["internal_execution_time"] = (endTime - startTime).TotalSeconds;
                result["status"] = "success";

                return Ok(result);
            }
            catch (Exception ex)
            {
                return Problem($"Failed to read all blobs for all collections: {ex.Message}");
            }
        }

        /// <summary>
        /// Route to the /upload-single-blob POST endpoint
        /// </summary>
        [HttpPost("upload-single-blob")]
        public async Task<IActionResult> UploadSingleBlob([FromForm] string collectionName,
                                                            [FromForm] string blobRelativePath,
                                                            IFormFile file)
        {
            try
            {
                var startTime = DateTime.UtcNow;

                Dictionary<string, object> uploadResult = await _storageManagementService.UploadSingleBlob(collectionName,
                                                                                                        blobRelativePath,
                                                                                                        file);
                
                var endTime = DateTime.UtcNow;

                Dictionary<string, object> result = new Dictionary<string, object>();
                result["internal_execution_time"] = (endTime - startTime).TotalSeconds;
                result["status"] = "success";
                result["upload_details"] = uploadResult;

                return Ok(result);
            }
            catch (Exception ex)
            {
                return Problem($"Failed to upload blobs to collection: {ex.Message}");
            }
        }

        /// <summary>
        /// Route to the /download-single-blob POST endpoint
        /// </summary>
        [HttpPost("download-single-blob")]
        public async Task<IActionResult> DownloadSingleBlob([FromBody] SingleBlobRequest request)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                
                Dictionary<string, object> uploadResult = await _storageManagementService.DownloadSingleBlob(request.pathInContainer.ToString());
                
                var endTime = DateTime.UtcNow;

                Dictionary<string, object> result = new Dictionary<string, object>();
                result["internal_execution_time"] = (endTime - startTime).TotalSeconds;
                result["status"] = "success";
                result["upload_details"] = uploadResult;

                return Ok(result);
            }
            catch (Exception ex)
            {
                return Problem($"Failed to upload blobs to collection: {ex.Message}");
            }
        }
    }
}
