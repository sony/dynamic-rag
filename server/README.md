# C#/.NET PgVector API Server

A production-ready C# .NET REST API for working with vector embeddings using PostgreSQL with PgVector extension. This application provides a robust foundation for building AI-powered applications that require vector similarity search, document processing, and multi-modal embeddings.

![System Block Diagram](./../systemBlock.png)

## ✨ Features

- **Vector Embeddings**: Support for text and image embeddings using Azure OpenAI models
- **Document Processing**: Automated parsing and chunking of various file formats (PDF, DOCX, CSV, etc.)
- **Vector Search**: High-performance approximate nearest neighbor (ANN) search
- **Multi-modal Support**: Text and image processing capabilities
- **Blob Storage**: Integration with Azure Blob Storage for document management
- **RESTful API**: Well-documented endpoints for all operations

## 🔧 Prerequisites

Before you begin, ensure you have the following installed:

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download) or later
- [PostgreSQL](https://www.postgresql.org/) (version 16.4 recommended) with [PgVector extension](https://github.com/pgvector/pgvector)
- Access to Azure OpenAI services (for embeddings and chat models)
- Azure Blob Storage account (for document storage)

## 🚀 Quick Start

### 1. Environment Configuration

Create a `.env` file in the server directory with your configuration. Choose one of the two configuration methods:

#### Option A: Environment Variables (Recommended)
```bash
# Model configuration
MODEL_SOURCE=envars
PROVIDER=azure

# Database configuration
PG_HOST=localhost
PG_DATABASE=postgres
PG_USER=your_username
PG_PASSWORD=your_password
PG_PORT=5432

# Application settings
PORT=5272
SSL_VERIFY=True

# Azure Blob Storage
BLOB_STORAGE_CONNECTION_STRING=DefaultEndpointsProtocol=https;AccountName=...
BLOB_STORAGE_RAG_CONTAINER_NAME=dynamicrag

# Processing configuration
MAX_RESURSIVE_SPLIT_DEPTH=100
CHUNKING_PARALLELISM=5

# Required AI Models
CHAT_MODEL_DEPLOYMENT_NAME=gpt-4o
CHAT_MODEL_ENDPOINT=https://your-resource.openai.azure.com/
CHAT_MODEL_API_KEY=your_api_key

EMBEDDING_MODEL_DEPLOYMENT_NAME=text-embedding-3-small
EMBEDDING_MODEL_ENDPOINT=https://your-resource.openai.azure.com/
EMBEDDING_MODEL_API_KEY=your_api_key

IMAGE_CAPTION_MODEL_DEPLOYMENT_NAME=gpt-4o
IMAGE_CAPTION_MODEL_ENDPOINT=https://your-resource.openai.azure.com/
IMAGE_CAPTION_MODEL_API_KEY=your_api_key

# Optional: CLIP model for image embeddings
CLIP_MODEL_DEPLOYMENT_NAME=openai-clip-image-text-embedd-3
CLIP_MODEL_ENDPOINT=https://your-ml-endpoint.azureml.net/
CLIP_MODEL_ENDPOINT_API_KEY=your_api_key
```

#### Option B: Azure Subscription Discovery
```bash
MODEL_SOURCE=azure
AZURE_SUBSCRIPTION_ID=your_subscription_id
# ... other required variables (PG_*, BLOB_STORAGE_*, etc.)
```

> 📝 **Note**: For detailed environment variable configuration, see [README_ENV_VARS.md](./README_ENV_VARS.md)

### 2. Build and Run

Navigate to the server directory and build the project:

```bash
cd server
dotnet build
```

#### Development Mode
Run the server in development mode (uses `appsettings.Development.json`):

```bash
dotnet run
```

#### Production Mode
Run the server in production mode (uses `appsettings.Production.json`):

```bash
dotnet run --environment Production
```

The API will be available at **http://localhost:5272**

## 🧪 Testing the API

### 1. Test Database Connection

First, verify your database connection is working:

**GET** `http://localhost:5272/test-connection`

```bash
curl -X GET http://localhost:5272/test-connection
```

**Expected Response:**
```json
{
    "success": true,
    "message": "Connected to PostgreSQL database successfully",
    "result": 1
}
```

### 2. Create Sample Embeddings

Generate sample embeddings for testing vector operations:

**POST** `http://localhost:5272/run-create-sample-embeddings`

```bash
curl -X POST http://localhost:5272/run-create-sample-embeddings \
  -H "Content-Type: application/json" \
  -d '{
    "nEmbeddings": 1000,
    "dimension": 1536
  }'
```

**Request Body:**
```json
{
    "nEmbeddings": 1000,
    "dimension": 1536
}
```

**Expected Response:**
```json
{
    "success": true,
    "internal_execution_time": "00:00:00.071",
    "message": "Sample embeddings created successfully"
}
```

### 3. Approximate Nearest Neighbors (ANN) Search

Test vector similarity search:

**POST** `http://localhost:5272/run-sample-ann`

```bash
curl -X POST http://localhost:5272/run-sample-ann \
  -H "Content-Type: application/json" \
  -d '{
    "dimension": 1536,
    "nNeighbors": 10
  }'
```

**Request Body:**
```json
{
    "dimension": 1536,
    "nNeighbors": 10
}
```

**Expected Response:**
```json
{
    "success": true,
    "internal_execution_time": 1.359501838684082,
    "neighbors": "<list of nearest vector indices and values>",
    "message": "ANN search run successfully"
}
```

## 🔍 Troubleshooting

### Common Issues

#### Database Connection Failed
- Verify PostgreSQL is running and accessible
- Check that PgVector extension is installed: `CREATE EXTENSION IF NOT EXISTS vector;`
- Ensure your database credentials in `.env` are correct
- Confirm the database exists and user has proper permissions

#### SSL/TLS Certificate Issues
- Set `SSL_VERIFY=False` in your `.env` file for development (not recommended for production)
- Ensure your certificates are properly configured for production environments

#### Model Configuration Errors
- Verify your Azure OpenAI endpoint URLs and API keys
- Check that the specified model deployment names exist in your Azure OpenAI resource
- Ensure your Azure subscription has access to the required models

#### Port Already in Use
- Change the `PORT` value in your `.env` file to an available port
- Kill any existing processes using port 5272: `lsof -ti:5272 | xargs kill -9`

### Environment Variable Validation

The application validates required environment variables on startup. If you see validation errors:

1. Check that all required variables are set in your `.env` file
2. Verify the syntax matches the examples above
3. Ensure no extra spaces or quotes around values (unless the value itself contains spaces)

## 📚 Additional Resources

- [Environment Variables Documentation](./README_ENV_VARS.md) - Detailed configuration guide
- [PgVector Documentation](https://github.com/pgvector/pgvector) - PostgreSQL vector extension
- [Azure OpenAI Documentation](https://docs.microsoft.com/en-us/azure/cognitive-services/openai/) - AI model configuration

## 🤝 Contributing

This project serves as a launchpad for evaluating PostgreSQL vector operations performance. Feel free to extend it with additional features or optimizations for your specific use case.