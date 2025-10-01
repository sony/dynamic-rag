using PgVectorDynamicRAG.Models;

namespace PgVectorDynamicRAG.Services
{
    /// <summary>
    /// Service for loading model configurations from environment variables as an alternative to Azure deployment discovery.
    /// </summary>
    public class EnvironmentModelService
    {
        /// <summary>
        /// Gets model configurations from environment variables for the specified model type.
        /// Expected environment variables:
        /// - For Chat models: CHAT_MODEL_DEPLOYMENT_NAME, CHAT_MODEL_ENDPOINT, CHAT_MODEL_API_KEY, CHAT_MODEL_VERSION (optional)
        /// - For Embedding models: EMBEDDING_MODEL_DEPLOYMENT_NAME, EMBEDDING_MODEL_ENDPOINT, EMBEDDING_MODEL_API_KEY, EMBEDDING_MODEL_VERSION (optional)
        /// </summary>
        /// <param name="modelType">The type of models to retrieve (ChatCompletion, TextEmbedding, ImageEmbedding, ImageCaptioning)</param>
        /// <returns>List of model configurations matching the Azure deployment service format</returns>
        public static List<Dictionary<string, object>> GetModelsFromEnvironment(string modelType)
        {
            var modelList = new List<Dictionary<string, object>>();

            switch (modelType)
            {
                case "ChatCompletion":
                    var chatModel = GetChatModelFromEnvironment();
                    if (chatModel != null) modelList.Add(chatModel);
                    break;

                case "TextEmbedding":
                    var embeddingModel = GetEmbeddingModelFromEnvironment();
                    if (embeddingModel != null) modelList.Add(embeddingModel);
                    break;

                case "ImageEmbedding":
                    // CLIP model is already handled separately in Program.cs, but we can support additional image embedding models
                    var imageEmbeddingModel = GetImageEmbeddingModelFromEnvironment();
                    if (imageEmbeddingModel != null) modelList.Add(imageEmbeddingModel);
                    break;

                case "ImageCaptioning":
                    var imageCaptionModel = GetImageCaptionModelFromEnvironment();
                    if (imageCaptionModel != null) modelList.Add(imageCaptionModel);
                    break;

                default:
                    Console.WriteLine($"WARNING: Model type '{modelType}' is not supported for environment variable configuration.");
                    break;
            }

            return modelList;
        }

        /// <summary>
        /// Gets chat model configuration from environment variables.
        /// </summary>
        private static Dictionary<string, object>? GetChatModelFromEnvironment()
        {
            var deploymentName = Environment.GetEnvironmentVariable("CHAT_MODEL_DEPLOYMENT_NAME");
            var endpoint = Environment.GetEnvironmentVariable("CHAT_MODEL_ENDPOINT");
            var apiKey = Environment.GetEnvironmentVariable("CHAT_MODEL_API_KEY");
            var modelVersion = Environment.GetEnvironmentVariable("CHAT_MODEL_VERSION") ?? "1";
            var provider = (Environment.GetEnvironmentVariable("PROVIDER") ?? "azure").ToLowerInvariant();

            bool requireEndpointAndKey = provider == "azure";
            if (string.IsNullOrEmpty(deploymentName) || (requireEndpointAndKey && (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))))
            {
                Console.WriteLine("INFO: Skipping chat model from environment variables. Required variables: CHAT_MODEL_DEPLOYMENT_NAME, CHAT_MODEL_ENDPOINT, CHAT_MODEL_API_KEY");
                LogMissingVariables("chat", deploymentName, endpoint, apiKey);
                return null;
            }

            // Validate that the deployment name matches a supported model
            var supportedModels = ModelDetails.GetModelDetailsByType("ChatCompletion");
            var modelDetails = supportedModels.FirstOrDefault(m => 
                string.Equals(m["ModelId"]?.ToString(), deploymentName, StringComparison.OrdinalIgnoreCase));

            if (modelDetails == null)
            {
                Console.WriteLine($"WARNING: Chat model '{deploymentName}' is not supported. Supported models: {string.Join(", ", supportedModels.Select(m => m["ModelId"]))}");
                return null;
            }

            Console.WriteLine($"INFO: Loaded chat model '{deploymentName}' from environment variables.");

            return new Dictionary<string, object>
            {
                { "DeploymentName", deploymentName },
                { "Endpoint", endpoint ?? string.Empty },
                { "ApiKey", apiKey ?? string.Empty },
                { "ServiceId", deploymentName },
                { "ModelVersion", modelVersion },
                { "Modality", "ChatCompletion" },
                { "Dimension", modelDetails["Dimension"] ?? 0 }
            };
        }

        /// <summary>
        /// Gets text embedding model configuration from environment variables.
        /// </summary>
        private static Dictionary<string, object>? GetEmbeddingModelFromEnvironment()
        {
            var deploymentName = Environment.GetEnvironmentVariable("EMBEDDING_MODEL_DEPLOYMENT_NAME");
            var endpoint = Environment.GetEnvironmentVariable("EMBEDDING_MODEL_ENDPOINT");
            var apiKey = Environment.GetEnvironmentVariable("EMBEDDING_MODEL_API_KEY");
            var modelVersion = Environment.GetEnvironmentVariable("EMBEDDING_MODEL_VERSION") ?? "1";
            var provider = (Environment.GetEnvironmentVariable("PROVIDER") ?? "azure").ToLowerInvariant();

            bool requireEndpointAndKey = provider == "azure";
            if (string.IsNullOrEmpty(deploymentName) || (requireEndpointAndKey && (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))))
            {
                Console.WriteLine("INFO: Skipping text embedding model from environment variables. Required variables: EMBEDDING_MODEL_DEPLOYMENT_NAME, EMBEDDING_MODEL_ENDPOINT, EMBEDDING_MODEL_API_KEY");
                LogMissingVariables("embedding", deploymentName, endpoint, apiKey);
                return null;
            }

            // Validate that the deployment name matches a supported model
            var supportedModels = ModelDetails.GetModelDetailsByType("TextEmbedding");
            var modelDetails = supportedModels.FirstOrDefault(m => 
                string.Equals(m["ModelId"]?.ToString(), deploymentName, StringComparison.OrdinalIgnoreCase));

            if (modelDetails == null)
            {
                Console.WriteLine($"WARNING: Text embedding model '{deploymentName}' is not supported. Supported models: {string.Join(", ", supportedModels.Select(m => m["ModelId"]))}");
                return null;
            }

            Console.WriteLine($"INFO: Loaded text embedding model '{deploymentName}' from environment variables.");

            return new Dictionary<string, object>
            {
                { "DeploymentName", deploymentName },
                { "Endpoint", endpoint ?? string.Empty },
                { "ApiKey", apiKey ?? string.Empty },
                { "ServiceId", deploymentName },
                { "ModelVersion", modelVersion },
                { "Modality", "TextEmbedding" },
                { "Dimension", modelDetails["Dimension"] ?? 1536 }
            };
        }

        /// <summary>
        /// Gets image embedding model configuration from environment variables.
        /// </summary>
        private static Dictionary<string, object>? GetImageEmbeddingModelFromEnvironment()
        {
            var deploymentName = Environment.GetEnvironmentVariable("IMAGE_EMBEDDING_MODEL_DEPLOYMENT_NAME");
            var endpoint = Environment.GetEnvironmentVariable("IMAGE_EMBEDDING_MODEL_ENDPOINT");
            var apiKey = Environment.GetEnvironmentVariable("IMAGE_EMBEDDING_MODEL_API_KEY");
            var modelVersion = Environment.GetEnvironmentVariable("IMAGE_EMBEDDING_MODEL_VERSION") ?? "3";

            if (string.IsNullOrEmpty(deploymentName) || string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
            {
                Console.WriteLine("INFO: Skipping image embedding model from environment variables. Required variables: IMAGE_EMBEDDING_MODEL_DEPLOYMENT_NAME, IMAGE_EMBEDDING_MODEL_ENDPOINT, IMAGE_EMBEDDING_MODEL_API_KEY");
                LogMissingVariables("image embedding", deploymentName, endpoint, apiKey);
                return null;
            }

            // Validate that the deployment name matches a supported model
            var supportedModels = ModelDetails.GetModelDetailsByType("ImageEmbedding");
            var modelDetails = supportedModels.FirstOrDefault(m => 
                string.Equals(m["ModelId"]?.ToString(), deploymentName, StringComparison.OrdinalIgnoreCase));

            if (modelDetails == null)
            {
                Console.WriteLine($"WARNING: Image embedding model '{deploymentName}' is not supported. Supported models: {string.Join(", ", supportedModels.Select(m => m["ModelId"]))}");
                return null;
            }

            Console.WriteLine($"INFO: Loaded image embedding model '{deploymentName}' from environment variables.");

            return new Dictionary<string, object>
            {
                { "DeploymentName", deploymentName },
                { "Endpoint", endpoint },
                { "ApiKey", apiKey },
                { "ServiceId", deploymentName },
                { "ModelVersion", modelVersion },
                { "Modality", "ImageEmbedding" },
                { "Dimension", modelDetails["Dimension"] ?? 768 }
            };
        }

        /// <summary>
        /// Gets image captioning model configuration from environment variables.
        /// </summary>
        private static Dictionary<string, object>? GetImageCaptionModelFromEnvironment()
        {
            var deploymentName = Environment.GetEnvironmentVariable("IMAGE_CAPTION_MODEL_DEPLOYMENT_NAME");
            var endpoint = Environment.GetEnvironmentVariable("IMAGE_CAPTION_MODEL_ENDPOINT");
            var apiKey = Environment.GetEnvironmentVariable("IMAGE_CAPTION_MODEL_API_KEY");
            var modelVersion = Environment.GetEnvironmentVariable("IMAGE_CAPTION_MODEL_VERSION") ?? "1";
            var provider = (Environment.GetEnvironmentVariable("PROVIDER") ?? "azure").ToLowerInvariant();

            bool requireEndpointAndKey = provider == "azure";
            if (string.IsNullOrEmpty(deploymentName) || (requireEndpointAndKey && (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))))
            {
                Console.WriteLine("INFO: Skipping image captioning model from environment variables. Required variables: IMAGE_CAPTION_MODEL_DEPLOYMENT_NAME, IMAGE_CAPTION_MODEL_ENDPOINT, IMAGE_CAPTION_MODEL_API_KEY");
                LogMissingVariables("image captioning", deploymentName, endpoint, apiKey);
                return null;
            }

            // Validate that the deployment name matches a supported model
            var supportedModels = ModelDetails.GetModelDetailsByType("ImageCaptioning");
            var modelDetails = supportedModels.FirstOrDefault(m => 
                string.Equals(m["ModelId"]?.ToString(), deploymentName, StringComparison.OrdinalIgnoreCase));

            if (modelDetails == null)
            {
                Console.WriteLine($"WARNING: Image captioning model '{deploymentName}' is not supported. Supported models: {string.Join(", ", supportedModels.Select(m => m["ModelId"]))}");
                return null;
            }

            Console.WriteLine($"INFO: Loaded image captioning model '{deploymentName}' from environment variables.");

            return new Dictionary<string, object>
            {
                { "DeploymentName", deploymentName },
                { "Endpoint", endpoint ?? string.Empty },
                { "ApiKey", apiKey ?? string.Empty },
                { "ServiceId", deploymentName },
                { "ModelVersion", modelVersion },
                { "Modality", "ImageCaptioning" },
                { "Dimension", modelDetails["Dimension"] ?? 0 }
            };
        }

        /// <summary>
        /// Logs which environment variables are missing for a model type.
        /// </summary>
        private static void LogMissingVariables(string modelType, string? deploymentName, string? endpoint, string? apiKey)
        {
            var missing = new List<string>();
            if (string.IsNullOrEmpty(deploymentName)) missing.Add($"{modelType.ToUpper()}_MODEL_DEPLOYMENT_NAME");
            if (string.IsNullOrEmpty(endpoint)) missing.Add($"{modelType.ToUpper()}_MODEL_ENDPOINT");
            if (string.IsNullOrEmpty(apiKey)) missing.Add($"{modelType.ToUpper()}_MODEL_API_KEY");

            if (missing.Any())
            {
                Console.WriteLine($"  Missing variables: {string.Join(", ", missing)}");
            }
        }

        /// <summary>
        /// Validates that required environment variables are set for at least one chat model, one embedding model, and one image caption model.
        /// This ensures the application can function with environment variable configuration.
        /// </summary>
        /// <returns>True if minimum required models are configured, false otherwise</returns>
        public static bool ValidateMinimumConfiguration()
        {
            var provider = (Environment.GetEnvironmentVariable("PROVIDER") ?? "azure").ToLowerInvariant();
            bool requireEndpointAndKey = provider == "azure";

            var hasChatModel = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CHAT_MODEL_DEPLOYMENT_NAME")) &&
                               (!requireEndpointAndKey || (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CHAT_MODEL_ENDPOINT")) &&
                                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CHAT_MODEL_API_KEY"))));

            var hasEmbeddingModel = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("EMBEDDING_MODEL_DEPLOYMENT_NAME")) &&
                                     (!requireEndpointAndKey || (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("EMBEDDING_MODEL_ENDPOINT")) &&
                                      !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("EMBEDDING_MODEL_API_KEY"))));

            var hasImageCaptionModel = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IMAGE_CAPTION_MODEL_DEPLOYMENT_NAME")) &&
                                        (!requireEndpointAndKey || (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IMAGE_CAPTION_MODEL_ENDPOINT")) &&
                                         !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IMAGE_CAPTION_MODEL_API_KEY"))));

            if (!hasChatModel || !hasEmbeddingModel || !hasImageCaptionModel)
            {
                Console.WriteLine("ERROR: When using MODEL_SOURCE=envars, you must configure at least one chat model, one embedding model, and one image caption model.");
                if (!hasChatModel)
                {
                    Console.WriteLine("  Missing chat model configuration: CHAT_MODEL_DEPLOYMENT_NAME, CHAT_MODEL_ENDPOINT, CHAT_MODEL_API_KEY");
                }
                if (!hasEmbeddingModel)
                {
                    Console.WriteLine("  Missing embedding model configuration: EMBEDDING_MODEL_DEPLOYMENT_NAME, EMBEDDING_MODEL_ENDPOINT, EMBEDDING_MODEL_API_KEY");
                }
                if (!hasImageCaptionModel)
                {
                    Console.WriteLine("  Missing image caption model configuration: IMAGE_CAPTION_MODEL_DEPLOYMENT_NAME, IMAGE_CAPTION_MODEL_ENDPOINT, IMAGE_CAPTION_MODEL_API_KEY");
                }
                return false;
            }

            return true;
        }
    }
} 