using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Management.ResourceManager;
using Microsoft.Azure.Management.ResourceManager.Models;
using Microsoft.Azure.Management.CognitiveServices;
using Microsoft.Azure.Management.CognitiveServices.Models;
using Microsoft.Rest;
using Newtonsoft.Json.Linq;
using PgVectorDynamicRAG.Models;


namespace PgVectorDynamicRAG.Services
{
    // Adapter to bridge Azure.Identity's TokenCredential with Microsoft.Rest.ServiceClientCredentials.
    public class TokenCredentialAdapter : ServiceClientCredentials
    {
        private readonly TokenCredential _tokenCredential;
        private readonly string[] _scopes;

        public TokenCredentialAdapter(TokenCredential tokenCredential, string[] scopes = null)
        {
            _tokenCredential = tokenCredential;
            // For Azure Resource Manager calls, the default scope is as follows.
            _scopes = scopes ?? new[] { "https://management.azure.com/.default" };
        }

        public override async Task ProcessHttpRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = await _tokenCredential.GetTokenAsync(new TokenRequestContext(_scopes), cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            await base.ProcessHttpRequestAsync(request, cancellationToken);
        }
    }

    public class DeploymentService
    {
        // Allowed model types.
        private readonly List<string> AllowedModelTypes = new List<string>
        {
            "ChatCompletion",
            "ImageCaptioning",
            "TextEmbedding",
            "ImageEmbedding"
        };

        /// <summary>
        /// Validates that the model type is one of the allowed types.
        /// </summary>
        private List<Dictionary<string, object>> ValidateModelType(string modelType)
        {
            if (!AllowedModelTypes.Contains(modelType))
            {
                throw new ArgumentException($"Model type must be one of: {string.Join(", ", AllowedModelTypes)}", nameof(modelType));
            }

            List<Dictionary<string, object>> acceptedModels = ModelDetails.GetModelDetailsByType(modelType);
            return acceptedModels;
        }

    /// <summary>
    /// Validates the model category and returns the list of accepted model names for that category.
    /// Accepted model categories and names:
    ///     "chatCompletions": "gpt-4", "gpt-40o", "o1"
    ///     "ImageCaptioning": "gpt-4o"
    ///     "embeddings": "ada002", "ada003-small", "ada003-larghe"
    /// </summary>
    /// <param name="modelCategory">The model category (e.g., "chatCompletions", "ImageCaptioning", or "embeddings").</param>
    /// <returns>A list of accepted model names for the specified category.</returns>
    /// <exception cref="ArgumentException">Thrown if the provided category is not accepted.</exception>
    // private List<string> FilterTestedModels(string modelCategory)
    // {
    //     // Define accepted model names for each category (case-insensitive).
    //     var acceptedModels = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
    //     {
    //         { "chatCompletions", new List<string> { "gpt-4", "gpt-4o", "o1" } },
    //         { "ImageCaptioning", new List<string> { "gpt-4o" } },
    //         { "embeddings", new List<string> { "text-embedding-ada-002", "text-embedding-3-small", "text-embedding-3-large" } }
    //     };

    //     if (!acceptedModels.ContainsKey(modelCategory))
    //     {
    //         // If the model category is not recognized, throw an exception with the accepted categories.
    //         throw new ArgumentException(
    //             $"Invalid model type '{modelCategory}'. Accepted model categories are: " +
    //             $"{string.Join(", ", acceptedModels.Keys)}");
    //     }

    //     // Return the list of accepted models for this category.
    //     return acceptedModels[modelCategory];
    // }

        /// <summary>
        /// Extracts the resource group name from a resource ID.
        /// Example resource ID format:
        /// /subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.CognitiveServices/accounts/{accountName}
        /// </summary>
        private string ExtractResourceGroup(string resourceId)
        {
            var marker = "/resourceGroups/";
            var startIndex = resourceId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (startIndex == -1)
            {
                return null;
            }
            startIndex += marker.Length;
            var endIndex = resourceId.IndexOf("/", startIndex, StringComparison.OrdinalIgnoreCase);
            if (endIndex == -1)
            {
                return resourceId.Substring(startIndex);
            }
            return resourceId.Substring(startIndex, endIndex - startIndex);
        }

        /// <summary>
        /// Reads the list of available OpenAI deployments from the given subscription.
        /// Returns a list of dictionaries where each dictionary contains:
        /// "modelName", "endpoint", "resourceGroup", "accountName", and "apiKey".
        /// </summary>
        public async Task<List<Dictionary<string, object>>> ListOAIDeployments(string subscriptionId, string modelType)
        {
            // Validate the model type. This method should throw if modelType is not allowed.
            List<Dictionary<string, object>> acceptedModels = ValidateModelType(modelType);

            var modelList = new List<Dictionary<string, object>>();

            // Use DefaultAzureCredential from Azure.Identity.
            var credential = new DefaultAzureCredential();
            var adapter = new TokenCredentialAdapter(credential);

            // Create the Resource Management client.
            using (var resourceClient = new ResourceManagementClient(adapter))
            {
                resourceClient.SubscriptionId = subscriptionId;
                string filter = "resourceType eq 'Microsoft.CognitiveServices/accounts'";

                // Use ListWithHttpMessagesAsync to retrieve the resources
                var response = await resourceClient.Resources.ListWithHttpMessagesAsync(filter);
                var resources = response.Body;

                foreach (var resource in resources)
                {
                    // Process only resources of kind "OpenAI"
                    if (!string.Equals(resource.Kind, "OpenAI", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Extract resource group and account name.
                    string resourceGroup = ExtractResourceGroup(resource.Id);
                    string accountName = resource.Name;

                    // Get detailed resource info (using API version 2024-10-01).
                    var resourceDetails = await resourceClient.Resources.GetByIdAsync(resource.Id, "2024-10-01");
                    string endpoint = null;
                    if (resourceDetails.Properties != null)
                    {
                        // Try to parse Properties as a JObject to extract the endpoint.
                        if (resourceDetails.Properties is JObject jObj)
                        {
                            endpoint = jObj.Value<string>("endpoint");
                        }
                        // Fallback: if Properties is a dictionary.
                        else if (resourceDetails.Properties is IDictionary<string, object> props &&
                                 props.TryGetValue("endpoint", out var ep))
                        {
                            endpoint = ep?.ToString();
                        }
                    }

                    // Create the Cognitive Services management client.
                    using (var cognitiveClient = new CognitiveServicesManagementClient(adapter) { SubscriptionId = subscriptionId })
                    {
                        // Get the account keys.
                        var keys = await cognitiveClient.Accounts.ListKeysAsync(resourceGroup, accountName);
                        string primaryKey = keys.Key1;

                        // List deployments for this account.
                        var deployments = await cognitiveClient.Deployments.ListAsync(resourceGroup, accountName);
                        foreach (var deployment in deployments)
                        {
                            // For debugging: print out deployment property names and values.
                            // if (deployment.Properties != null)
                            // {
                            //     Console.WriteLine($"Deployment Name: {deployment.Name}");
                            //     Console.WriteLine("Properties:");
                            //     foreach (var propertyInfo in deployment.Properties.GetType().GetProperties())
                            //     {
                            //         Console.WriteLine($"  {propertyInfo.Name}: {propertyInfo.GetValue(deployment.Properties)}");
                            //     }
                            // }

                            // Raise pass and print an INFO log message if the deployment.Name is not in acceptedModels
                            if (!acceptedModels.Any(model => string.Equals(model["modelId"]?.ToString(), deployment.Name, StringComparison.OrdinalIgnoreCase)))
                            {
                                // You can use your preferred logging library here, e.g., Serilog, NLog, etc.
                                Console.WriteLine($"INFO: Deployment '{deployment.Name}' is not in the list of accepted {modelType} models.");
                                continue;
                            }

                            var modelDetailDict = acceptedModels.First(model => string.Equals(model["modelId"]?.ToString(), deployment.Name, StringComparison.OrdinalIgnoreCase));
                            
                            // Parse metadata from the Model property.
                            // Since AdditionalProperties is not available, we will extract metadata from deployment.Properties.Model.
                            Dictionary<string, object> metadata = new Dictionary<string, object>();
                            if (deployment.Properties?.Model != null)
                            {
                                var modelObject = deployment.Properties.Model;
                                foreach (var prop in modelObject.GetType().GetProperties())
                                {
                                    metadata[prop.Name] = prop.GetValue(modelObject);
                                }
                            }

                            // Optionally, you can use metadata to filter deployments based on the desired modelType.
                            // For example, if your DeploymentModel contains a property named "ModelType" that indicates its capability:
                            //
                            // if (metadata.TryGetValue("ModelType", out var modelTypeValue) &&
                            //     !string.Equals(modelTypeValue?.ToString(), modelType, StringComparison.OrdinalIgnoreCase))
                            // {
                            //     continue; // Skip this deployment if it doesn't match.
                            // }

                            if (acceptedModels.Any(model => string.Equals(model["modelId"]?.ToString(), deployment.Name, StringComparison.OrdinalIgnoreCase)))
                            {
                                // Prevent duplicate entries (by deployment/model name).
                                if (!modelList.Any(d =>
                                    d.ContainsKey("DeploymentName") &&
                                    string.Equals(d["DeploymentName"].ToString(), deployment.Name, StringComparison.OrdinalIgnoreCase)))
                                {
                                    modelList.Add(new Dictionary<string, object>
                                    {
                                        { "DeploymentName", deployment.Name },
                                        { "Endpoint", endpoint },
                                        { "ResourceGroup", resourceGroup },
                                        { "AccountName", accountName },
                                        { "ApiKey", primaryKey },
                                        { "metadata", metadata },
                                        { "ModelVersion", metadata.ContainsKey("Version") ? metadata["Version"] : null },
                                        { "Modality", modelType },
                                        { "Dimension", modelDetailDict["Dimension"] }
                                    });
                                }
                            }
                        }
                    }
                }
            }
            return modelList;
        }
    }
}
