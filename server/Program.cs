using Microsoft.OpenApi.Models;
using PgVectorDynamicRAG.Services;
using PgVectorDynamicRAG.Factories;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Connectors.Amazon.Core;
using Microsoft.SemanticKernel.Connectors.Amazon;
using DotNetEnv;
using System.Reflection;
// using Microsoft.AspNetCore.Cors;
// using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
// using Azure.AI.OpenAI.Chat;
using Npgsql;
using Amazon.BedrockRuntime;
using Amazon.Extensions.NETCore.Setup;
// using Pgvector.Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

// using Microsoft.AspNetCore.Builder;
// using Microsoft.Extensions.DependencyInjection;

#pragma warning disable SKEXP0010 // 'Microsoft.SemanticKernel.AzureOpenAIKernelBuilderExtensions.AddAzureOpenAITextEmbeddingGeneration(Microsoft.SemanticKernel.IKernelBuilder, string, string, string, string?, string?, System.Net.Http.HttpClient?, int?, string?)' is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning disable SKEXP0020 // 'Microsoft.SemanticKernel.PostgresServiceCollectionExtensions.AddPostgresVectorStore(Microsoft.Extensions.DependencyInjection.IServiceCollection, string, Microsoft.SemanticKernel.Connectors.Postgres.PostgresVectorStoreOptions?, string?)' is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

var builder = WebApplication.CreateBuilder(args);



// SIMPLIFIED PORT BINDING FOR CONTAINERS
// This binds to all interfaces (0.0.0.0) on a port
// For Docker & Azure, always use PORT 8080 unless env var overrides it
var port = Environment.GetEnvironmentVariable("PORT") ?? "5272";

// Set Kestrel endpoint explicitly using the builder pattern without UseUrls
// builder.WebHost.ConfigureKestrel(options => 
// {
//     // Listen on all interfaces (0.0.0.0) with specified port
//     options.ListenAnyIP(int.Parse(port));
//     Console.WriteLine($"Kestrel configured to listen on ANY IP, port {port}");
// });

builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Determine the environment and source the correct appsettings profile
var environment = builder.Environment.IsDevelopment() ? "Development" : "Production";
builder.Configuration.AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true);

// Load environment variables from .env (if present)
Env.Load(); // This will load variables into Environment.GetEnvironmentVariable()
NpgsqlConnection.GlobalTypeMapper.UseVector();

// Create a Kernel builder
var kernelBuilder = Kernel.CreateBuilder();

// /// WARNING: This is a dangerous operation and should not be used in production code. ///
// var httpClientHandler = new HttpClientHandler
// {
//     ServerCertificateCustomValidationCallback =
//         HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
// };
// var httpClient = new HttpClient(httpClientHandler);
// kernelBuilder.Services.AddHttpClient("CustomClient").ConfigurePrimaryHttpMessageHandler(() => httpClientHandler);
// /// end warning ///


// Read SSL_VERIFY env var (defaults to True if not set)
// var sslVerify = Environment.GetEnvironmentVariable("SSL_VERIFY");
// bool disableSslVerification = !string.IsNullOrEmpty(sslVerify) && sslVerify.Equals("False", StringComparison.OrdinalIgnoreCase);

// // Configure HttpClientHandler based on SSL_VERIFY
// HttpClientHandler httpClientHandler;
// if (disableSslVerification)
// {
//     // WARNING: Disables SSL validation -- do not use in production!
//     httpClientHandler = new HttpClientHandler
//     {
//         ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
//     };
//     Console.WriteLine("WARNING: SSL certificate validation is DISABLED.");
// }
// else
// {
//     httpClientHandler = new HttpClientHandler(); // Default validation
//     Console.WriteLine("INFO: SSL certificate validation is ENABLED.");
// }

// // register the apps HttpClient with the custom handler
// builder.Services.AddHttpClient("CustomClient")
//     .ConfigurePrimaryHttpMessageHandler(() => httpClientHandler);

// register the kernel's HttpClient with the custom handler
// kernelBuilder.Services.AddHttpClient("CustomClient")
//     .ConfigurePrimaryHttpMessageHandler(() => httpClientHandler);

var sslVerify = Environment.GetEnvironmentVariable("SSL_VERIFY");
bool disableSslVerification =
    !string.IsNullOrEmpty(sslVerify) &&
    sslVerify.Equals("False", StringComparison.OrdinalIgnoreCase);

Console.WriteLine(disableSslVerification
  ? "WARNING: SSL certificate validation is DISABLED."
  : "INFO: SSL certificate validation is ENABLED.");

builder.Services.AddHttpClient("CustomClient", client =>
{
    // Optional: set your defaults here instead of per‐call
    // client.BaseAddress = new Uri(builder.Configuration["CLIP_MODEL_ENDPOINT"]);
    client.DefaultRequestHeaders.Accept.Add(
      new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
})
.SetHandlerLifetime(Timeout.InfiniteTimeSpan)
.ConfigurePrimaryHttpMessageHandler(() =>
{
    // *Each* time this runs you get a fresh handler*
    if (disableSslVerification)
    {
        return new HttpClientHandler {
            ServerCertificateCustomValidationCallback =
              HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
    }
    else
    {
        return new HttpClientHandler();
    }
});

// Also register the CustomClient in the kernel builder services
kernelBuilder.Services.AddHttpClient("CustomClient", client =>
{
    client.DefaultRequestHeaders.Accept.Add(
      new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
})
.SetHandlerLifetime(Timeout.InfiniteTimeSpan)
.ConfigurePrimaryHttpMessageHandler(() =>
{
    if (disableSslVerification)
    {
        return new HttpClientHandler {
            ServerCertificateCustomValidationCallback =
              HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
    }
    else
    {
        return new HttpClientHandler();
    }
});

// Add the vector store the Semantic Kernel services.
var host = Environment.GetEnvironmentVariable("PG_HOST");
var database = Environment.GetEnvironmentVariable("PG_DATABASE");
var username = Environment.GetEnvironmentVariable("PG_USER");
var password = Environment.GetEnvironmentVariable("PG_PASSWORD");
string postgresConnectionString = $"Host={host};Database={database};Username={username};Password={password}";
kernelBuilder.Services.AddPostgresVectorStore(postgresConnectionString);

// --------------------------Move to SchemaManagementService initialization/constructor?-------------------------------
// Initialize the score schema using public static methods from SchemaManagementService
bool initSchemaSuccess = await SchemaManagementService.InitializeCoreSchema(postgresConnectionString);

// Source, validate, and register embedding models based on MODEL_SOURCE
// var embeddingServices = builder.Configuration.GetSection("EmbeddingServices").Get<List<Dictionary<string, object>>>(); # SPEAI-6051
var modelSource = Environment.GetEnvironmentVariable("MODEL_SOURCE")?.ToLower() ?? "envars";
var provider = Environment.GetEnvironmentVariable("PROVIDER")?.ToLower() ?? "azure";
bool isAwsProvider = provider == "aws" || provider == "bedrock";
Console.WriteLine($"INFO: Using model source: {modelSource}");

List<Dictionary<string, object>> embeddingServices;
IAmazonBedrockRuntime? bedrockRuntime = null;

// If using AWS/Bedrock anywhere, create ONE Bedrock runtime client and register it once
if (isAwsProvider)
{
    var awsOptions = builder.Configuration.GetAWSOptions();
    builder.Services.AddDefaultAWSOptions(awsOptions);
    bedrockRuntime = awsOptions.CreateServiceClient<IAmazonBedrockRuntime>();
    builder.Services.AddSingleton<IAmazonBedrockRuntime>(bedrockRuntime);
}

// Embedding Models
if (modelSource == "envars" && provider == "azure") { // provider is irrelevant here
    // Validate minimum configuration for environment variables
    if (!EnvironmentModelService.ValidateMinimumConfiguration())
    {
        throw new InvalidOperationException("Invalid environment variable configuration. Cannot start application.");
    }
    embeddingServices = EnvironmentModelService.GetModelsFromEnvironment("TextEmbedding");
} else if (modelSource == "envars" && isAwsProvider) {
    embeddingServices = EnvironmentModelService.GetModelsFromEnvironment("TextEmbedding");
// TODO: Add Bedrock AWS account # auto retreive available models
} else if (modelSource == "subscription" && provider == "bedrock") {
    throw new NotImplementedException("MODEL_SOURCE=Subscription with PROVIDER=bedrock is not yet implemented. Only Azure subscription and environment variable sourcing are currently supported.");
} else if (modelSource == "subscription" && provider == "azure") {
    string subscriptionId = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID");
    if (string.IsNullOrEmpty(subscriptionId))
    {
        throw new InvalidOperationException("AZURE_SUBSCRIPTION_ID is required when MODEL_SOURCE=azure");
    }
    var azureDeploymentService = new DeploymentService();
    embeddingServices = await azureDeploymentService.ListOAIDeployments(subscriptionId, "TextEmbedding");
} else {
    throw new NotImplementedException("Only supporting environment variable for Azure and Bedrock, or subscription sourcing for Azure.");
}

// if (modelSource == "envars")
// {
//     // Validate minimum configuration for environment variables
//     if (!EnvironmentModelService.ValidateMinimumConfiguration())
//     {
//         throw new InvalidOperationException("Invalid environment variable configuration. Cannot start application.");
//     }
//     embeddingServices = EnvironmentModelService.GetModelsFromEnvironment("TextEmbedding");
// }
// else if (modelSource == "azure")
// {
//     string subscriptionId = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID");
//     if (string.IsNullOrEmpty(subscriptionId))
//     {
//         throw new InvalidOperationException("AZURE_SUBSCRIPTION_ID is required when MODEL_SOURCE=azure");
//     }
//     var azureDeploymentService = new AzureOpenAIDeploymentService();
//     embeddingServices = await azureDeploymentService.ListOAIDeployments(subscriptionId, "TextEmbedding");
// }
// else
// {
//     throw new InvalidOperationException($"Invalid MODEL_SOURCE value: {modelSource}. Supported values are 'azure' or 'envars'");
// }

var validatedEmbeddingServices = await SchemaManagementService.ValidateOrAddModels(embeddingServices, "TextEmbedding", postgresConnectionString);
Console.WriteLine("Validated Embedding Services:");
foreach (var service in validatedEmbeddingServices)
{
    var endpoint = service["Endpoint"] as string;
    var deploymentName = service["DeploymentName"] as string;
    // var apiKeyVariableName = service["ApiKeyVariableName"] as string;
    // var apiKey = Environment.GetEnvironmentVariable(apiKeyVariableName);
    var apiKey = service["ApiKey"] as string;
    var serviceId = service["ServiceId"] as string;

    // embeddingServices = await schemaManagementService.ValidateOrAddModels(embeddingServices, "embedding");
    // add the embedding services
    if (provider == "azure") {
        // Create HttpClient with SSL configuration directly
        var httpClient = new HttpClient(disableSslVerification 
            ? new HttpClientHandler { 
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator 
            }
            : new HttpClientHandler());
        
        kernelBuilder.AddAzureOpenAIEmbeddingGenerator(
            deploymentName: deploymentName,
            endpoint: endpoint,
            apiKey: apiKey,
            serviceId: serviceId,
            httpClient: httpClient
        );
    } else if (isAwsProvider) { // AWS Bedrock embeddings
        // Normalize Bedrock service IDs to avoid SK-added numeric suffixes later
        var normalizedServiceId = serviceId;
        kernelBuilder.AddBedrockEmbeddingGenerator(
            modelId: deploymentName,
            bedrockRuntime: bedrockRuntime,
            serviceId: normalizedServiceId
        );
    } else {
        throw new NotImplementedException("Only supporting Azure and Bedrock for embedding models.");
    }
}

// The CLIP embedding model is hosted on a dedicated compute endpoint. So we will need to add it manually
List<Dictionary<string, object>> imageEmbeddingServices = new List<Dictionary<string, object>>();
// Log and conditionally set up image embedding models
var clipDeploymentName = Environment.GetEnvironmentVariable("CLIP_MODEL_DEPLOYMENT_NAME");
var clipEndpoint = Environment.GetEnvironmentVariable("CLIP_MODEL_ENDPOINT");
var clipApiKey = Environment.GetEnvironmentVariable("CLIP_MODEL_ENDPOINT_API_KEY");

if (!string.IsNullOrEmpty(clipDeploymentName) &&
    !string.IsNullOrEmpty(clipEndpoint) &&
    !string.IsNullOrEmpty(clipApiKey))
{
    Console.WriteLine("INFO: All required environment variables for CLIP model (CLIP_MODEL_DEPLOYMENT_NAME, CLIP_MODEL_ENDPOINT, CLIP_MODEL_ENDPOINT_API_KEY) are set. Proceeding with image embedding model setup.");
    imageEmbeddingServices.Add(new Dictionary<string, object>
    {
        { "DeploymentName", clipDeploymentName },
        { "Endpoint", clipEndpoint },
        { "ApiKey", clipApiKey },
        { "ServiceId", clipDeploymentName }, // Using the retrieved variable for ServiceId as well
        { "ModelVersion", "3" },
        { "Modality", "ImageEmbedding"},
        { "Dimension", 768 }
    });
}
else
{
    Console.WriteLine("INFO: Skipping setup of image embedding models as one or more required environment variables are not set:");
    if (string.IsNullOrEmpty(clipDeploymentName))
    {
        Console.WriteLine("  - CLIP_MODEL_DEPLOYMENT_NAME is not set.");
    }
    if (string.IsNullOrEmpty(clipEndpoint))
    {
        Console.WriteLine("  - CLIP_MODEL_ENDPOINT is not set.");
    }
    if (string.IsNullOrEmpty(clipApiKey))
    {
        Console.WriteLine("  - CLIP_MODEL_ENDPOINT_API_KEY is not set.");
    }
}
// Validate and register the CLIP embedding model
var validatedImageEmbeddingServices = await SchemaManagementService.ValidateOrAddModels(imageEmbeddingServices, "ImageEmbedding", postgresConnectionString);

// Source, validate, and register the chat models based on MODEL_SOURCE
List<Dictionary<string, object>> chatServices;
// Chat Models (mirror embedding model branching)
if (modelSource == "envars" && provider == "azure")
{
    if (!EnvironmentModelService.ValidateMinimumConfiguration())
    {
        throw new InvalidOperationException("Invalid environment variable configuration. Cannot start application.");
    }
    chatServices = EnvironmentModelService.GetModelsFromEnvironment("ChatCompletion");
}
else if (modelSource == "envars" && isAwsProvider)
{
    chatServices = EnvironmentModelService.GetModelsFromEnvironment("ChatCompletion");
}
else if (modelSource == "subscription" && provider == "bedrock")
{
    throw new NotImplementedException("MODEL_SOURCE=Subscription with PROVIDER=bedrock is not yet implemented. Only Azure subscription and environment variable sourcing are currently supported.");
}
else if (modelSource == "subscription" && provider == "azure")
{
    string subscriptionId = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID");
    if (string.IsNullOrEmpty(subscriptionId))
    {
        throw new InvalidOperationException("AZURE_SUBSCRIPTION_ID is required when MODEL_SOURCE=azure");
    }
    var azureDeploymentService = new DeploymentService();
    chatServices = await azureDeploymentService.ListOAIDeployments(subscriptionId, "ChatCompletion");
}
else
{
    throw new NotImplementedException("Only supporting environment variable for Azure and Bedrock, or subscription sourcing for Azure.");
}

var validatedChatModels = await SchemaManagementService.ValidateOrAddModels(chatServices, "ChatCompletion", postgresConnectionString);
foreach (var model in validatedChatModels)
{
    var endpoint = model["Endpoint"] as string;
    var deploymentName = model["DeploymentName"] as string;
    // var apiKeyVariableName = model["ApiKeyVariableName"] as string;
    // var apiKey = Environment.GetEnvironmentVariable(apiKeyVariableName);
    var apiKey = model["ApiKey"] as string;
    var serviceId = model["ServiceId"] as string;

    // add the chat models
    if (provider == "azure")
    {
        // Create HttpClient with SSL configuration directly
        var httpClient = new HttpClient(disableSslVerification 
            ? new HttpClientHandler { 
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator 
            }
            : new HttpClientHandler());
        
        kernelBuilder.AddAzureOpenAIChatCompletion(
            deploymentName: deploymentName,
            endpoint: endpoint,
            apiKey: apiKey,
            serviceId: serviceId,
            httpClient: httpClient
        );
    }
    else if (isAwsProvider)
    {
        // Normalize Bedrock service ID similarly for chat
        var normalizedChatServiceId = serviceId;
        kernelBuilder.AddBedrockChatCompletionService(
            modelId: deploymentName,
            bedrockRuntime: bedrockRuntime,
            serviceId: normalizedChatServiceId
        );
    }
    else
    {
        throw new NotImplementedException("Only supporting Azure and Bedrock for chat models.");
    }
}

// Register the first ImageCaptioningService based on MODEL_SOURCE (mirror embedding model branching)
List<Dictionary<string, object>> imgCaptionServices;
if (modelSource == "envars" && provider == "azure")
{
    if (!EnvironmentModelService.ValidateMinimumConfiguration())
    {
        throw new InvalidOperationException("Invalid environment variable configuration. Cannot start application.");
    }
    imgCaptionServices = EnvironmentModelService.GetModelsFromEnvironment("ImageCaptioning");
}
else if (modelSource == "envars" && isAwsProvider)
{
    imgCaptionServices = EnvironmentModelService.GetModelsFromEnvironment("ImageCaptioning");
}
else if (modelSource == "subscription" && provider == "bedrock")
{
    throw new NotImplementedException("MODEL_SOURCE=Subscription with PROVIDER=bedrock is not yet implemented. Only Azure subscription and environment variable sourcing are currently supported.");
}
else if (modelSource == "subscription" && provider == "azure")
{
    string subscriptionId = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID");
    if (string.IsNullOrEmpty(subscriptionId))
    {
        throw new InvalidOperationException("AZURE_SUBSCRIPTION_ID is required when MODEL_SOURCE=azure");
    }
    var azureDeploymentService = new DeploymentService();
    imgCaptionServices = await azureDeploymentService.ListOAIDeployments(subscriptionId, "ImageCaptioning");
}
else
{
    throw new NotImplementedException("Only supporting environment variable for Azure and Bedrock, or subscription sourcing for Azure.");
}

if (imgCaptionServices.Count > 0)
{
    var imageCaptionModel = imgCaptionServices[0];
    // var imageCaptionModel = builder.Configuration.GetSection("ImageCaptioningService").Get<Dictionary<string, object>>();
    // add the chat models
    try
    {
        if (provider == "azure")
        {
            // Create HttpClient with SSL configuration directly
            var httpClient = new HttpClient(disableSslVerification 
                ? new HttpClientHandler { 
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator 
                }
                : new HttpClientHandler());
            
            kernelBuilder.AddAzureOpenAIChatCompletion(
                deploymentName: imageCaptionModel["DeploymentName"] as string,
                endpoint: imageCaptionModel["Endpoint"] as string,
                apiKey: imageCaptionModel["ApiKey"] as string,
                serviceId: "ImageCaptioningService",
                httpClient: httpClient
            );
        }
        else if (isAwsProvider)
        {
            kernelBuilder.AddBedrockChatCompletionService(
                modelId: imageCaptionModel["DeploymentName"] as string,
                serviceId: "ImageCaptioningService",
                bedrockRuntime: bedrockRuntime
            );
        }
        else
        {
            throw new NotImplementedException("Only supporting Azure and Bedrock for image captioning models.");
        }
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"Failed to configure image captioning service: {ex.Message}", ex);
    }
}
else
{
    throw new InvalidOperationException("No image captioning models are available. Image captioning service is required for application startup.");
}
// -----------------------end of what im considering moving to the contructor----------------------------------

// Build the Semantic Kernel
var kernel = kernelBuilder.Build();

// add DEVELOPMENT SERVICES: elementary operations
// builder.Services.AddSingleton<ConnectionService>();
// builder.Services.AddSingleton<EmbeddingService>();
// builder.Services.AddSingleton<AnnService>();

// add PRODUCTION SERVICES
builder.Services.AddSingleton(kernel);
// Default chat PromptExecutionSettings (applied app-wide)
builder.Services.AddSingleton(new PromptExecutionSettings
{
    ExtensionData = new Dictionary<string, object>
    {
        // Bedrock (Anthropic Claude)
        ["maxTokens"] = 1000,
        // Bedrock (Titan)
        ["maxTokenCount"] = 1000,
        // Common knobs
        ["temperature"] = 0.5,
        ["topP"] = 1.0,
        ["presencePenalty"] = 0.0,
        ["frequencyPenalty"] = 0.0
    }
});
builder.Services.AddTransient<IPostgresConnectionFactory, PostgresConnectionFactory>();
builder.Services.AddSingleton<SchemaManagementService>();
builder.Services.AddSingleton<TextEmbeddingService>();
builder.Services.AddSingleton<ImageEmbeddingService>();
builder.Services.AddSingleton<CollectionManagementService>();
builder.Services.AddSingleton<EmbeddingServiceFactory>();
if (provider == "azure") { // Azure Blob Storage
    // Validate required Azure environment variables
    var blobConnectionString = Environment.GetEnvironmentVariable("BLOB_STORAGE_CONNECTION_STRING");
    var blobContainerName = Environment.GetEnvironmentVariable("BLOB_STORAGE_RAG_CONTAINER_NAME");
    
    if (string.IsNullOrEmpty(blobConnectionString))
    {
        throw new InvalidOperationException("BLOB_STORAGE_CONNECTION_STRING environment variable is required when PROVIDER=azure");
    }
    if (string.IsNullOrEmpty(blobContainerName))
    {
        throw new InvalidOperationException("BLOB_STORAGE_RAG_CONTAINER_NAME environment variable is required when PROVIDER=azure");
    }
    
    Console.WriteLine($"INFO: Azure Blob Storage configured - Container: {blobContainerName}");
    builder.Services.AddSingleton<BlobManagementService>();
    builder.Services.AddSingleton<AbstractStorageService>(provider => provider.GetService<BlobManagementService>());
} else if (isAwsProvider) { // AWS S3
    // Validate required AWS environment variables
    var s3BucketName = Environment.GetEnvironmentVariable("S3_BUCKET_NAME");
    
    if (string.IsNullOrEmpty(s3BucketName))
    {
        throw new InvalidOperationException("S3_BUCKET_NAME environment variable is required when PROVIDER=aws or PROVIDER=bedrock");
    }
    
    // Check for AWS credentials (AWS SDK will use default credential chain)
    // This includes: environment variables, IAM roles, profiles, etc.
    var awsAccessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
    var awsSecretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
    var awsRegion = Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION") ?? Environment.GetEnvironmentVariable("AWS_REGION");
    
    // Check for custom S3 endpoint (for S3-compatible services or custom endpoints)
    var s3EndpointUrl = Environment.GetEnvironmentVariable("S3_ENDPOINT_URL");
    var s3StorageUrl = Environment.GetEnvironmentVariable("S3_STORAGE_URL");
    
    // If S3_STORAGE_URL is provided, try to extract region from it (if it's a console URL)
    if (!string.IsNullOrEmpty(s3StorageUrl) && string.IsNullOrEmpty(awsRegion))
    {
        var regionMatch = System.Text.RegularExpressions.Regex.Match(s3StorageUrl, @"region=([^&]+)");
        if (regionMatch.Success)
        {
            awsRegion = regionMatch.Groups[1].Value;
            Console.WriteLine($"INFO: Extracted AWS region '{awsRegion}' from S3_STORAGE_URL");
        }
    }
    
    if (string.IsNullOrEmpty(awsAccessKey) || string.IsNullOrEmpty(awsSecretKey))
    {
        Console.WriteLine("WARNING: AWS_ACCESS_KEY_ID and/or AWS_SECRET_ACCESS_KEY not found. AWS SDK will attempt to use other credential sources (IAM roles, profiles, etc.)");
    }
    
    if (string.IsNullOrEmpty(awsRegion))
    {
        Console.WriteLine("WARNING: AWS region not specified. Using default region 'us-west-2'");
        awsRegion = "us-west-2";
    }
    
    if (!string.IsNullOrEmpty(s3StorageUrl))
    {
        Console.WriteLine($"INFO: S3_STORAGE_URL provided (Console URL): {s3StorageUrl}");
    }
    
    Console.WriteLine($"INFO: AWS S3 configured - Bucket: {s3BucketName}, Region: {awsRegion}");
    
    // Register AWS S3 client with proper configuration
    builder.Services.AddSingleton<Amazon.S3.IAmazonS3>(provider => 
    {
        var config = new Amazon.S3.AmazonS3Config()
        {
            RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(awsRegion)
        };
        
        // Handle custom S3 endpoint if provided (for S3-compatible services)
        if (!string.IsNullOrEmpty(s3EndpointUrl))
        {
            config.ServiceURL = s3EndpointUrl;
            config.ForcePathStyle = true; // Required for most S3-compatible services
            Console.WriteLine($"INFO: Using custom S3 endpoint: {s3EndpointUrl}");
        }
        
        return new Amazon.S3.AmazonS3Client(config);
    });
    builder.Services.AddSingleton<S3ManagementService>();
    builder.Services.AddSingleton<AbstractStorageService>(provider => provider.GetService<S3ManagementService>());
} else {
    throw new NotImplementedException("Only supporting Azure and AWS for storage services.");
}
builder.Services.AddSingleton<ContextAwareChatService>();
builder.Services
    .AddMcpServer()
    // .WithStdioServerTransport() // enable only for local stdio support
    .WithHttpTransport()
    .WithToolsFromAssembly();

// add controllers for respective services
builder.Services.AddControllers();

// add Swagger services to the app builder
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "PgVector Dotnet Driver", Version = "1.0.0" });

    // Get the XML documentation file path
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath);
});

// builder.Services.AddEndpointsApiExplorer();
// builder.Services.AddSwaggerGen();
// builder.Services.AddAuthorization();

// builder.Services.AddCors(options =>
// {
//     options.AddDefaultPolicy(policy =>
//     {
//         policy.AllowAnyOrigin()
//               .AllowAnyMethod()
//               .AllowAnyHeader();
//     });
// });

// Configure CORS based on environment
// var corsOrigins = builder.Configuration.GetSection("CorsOrigins").Get<string[]>() ?? new[] { "*" };
// builder.Services.AddCors(options =>
// {
//     options.AddDefaultPolicy(policy =>
//     {
//         if (corsOrigins.Contains("*"))
//         {
//             policy.AllowAnyOrigin()
//                   .AllowAnyMethod()
//                   .AllowAnyHeader();
//         }
//         else
//         {
//             policy.WithOrigins(corsOrigins)
//                   .AllowAnyMethod()
//                   .AllowAnyHeader();
//         }
//     });
// });
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// build the application
var app = builder.Build();

// // Add middleware to handle SSE requests
// app.Map("/sse", async context =>
// {
//     context.Response.Headers.Add("Cache-Control", "no-cache");
//     context.Response.Headers.Add("Content-Type", "text/event-stream");

//     while (true)
//     {
//         await context.Response.WriteAsync($"data: {DateTime.Now}\n\n");
//         await context.Response.Body.FlushAsync();
//         await Task.Delay(1000); // Send updates every second
//     }
// });


// Enable swagger services
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "PgVector Dotnet Launchpad v1.0.0");
    c.RoutePrefix = string.Empty; // Set Swagger UI at the app's root
});

// configure controller routes
// Add CORS middleware early in the pipeline
app.UseCors();
// app.UseCors("AllowFrontend");
app.UseRouting();
app.UseAuthorization();
// app.UseHttpsRedirection();
app.MapControllers();


app.UseEndpoints(endpoints =>
{
    endpoints.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");
});

// Configure middleware based on environment
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    // Additional development-specific middleware
    Console.WriteLine("Running in Development mode");
}
else
{
    // Configure production error handling
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    Console.WriteLine("Running in Production mode");
}


app.MapGet("/", context =>
{
    context.Response.Redirect("/index.html");
    return Task.CompletedTask;
});

app.MapGet("/healthz", () => Results.Ok("OK"));

app.MapMcp();

ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
Console.WriteLine($"Min worker threads: {workerThreads}, completion port threads: {completionPortThreads}");

// Print available model services after initialization
// try
// {
//     using var scope = app.Services.CreateScope();
//     var schemaService = scope.ServiceProvider.GetRequiredService<SchemaManagementService>();

//     var embeddingModelsByModality = await schemaService.ReadEmbeddingModels();
//     var chatModels = await schemaService.ReadChatModels();

//     Console.WriteLine("Available Embedding Models (by modality):");
//     foreach (var kvp in embeddingModelsByModality)
//     {
//         Console.WriteLine($"  {kvp.Key}: {string.Join(", ", kvp.Value)}");
//     }

//     Console.WriteLine("Available Chat Models:");
//     foreach (var model in chatModels)
//     {
//         Console.WriteLine($"  {model}");
//     }
// }
// catch (Exception ex)
// {
//     Console.WriteLine($"Failed to read available services: {ex.Message}");
// }

app.Run();
