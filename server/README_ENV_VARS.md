# Environment Variable Configuration

This document describes how to configure the application using environment variables instead of Azure subscription discovery.

## Model Source Configuration

Set the `MODEL_SOURCE` environment variable to control how models are sourced:

- `MODEL_SOURCE=azure` (default): Discover models from Azure subscription
- `MODEL_SOURCE=envars`: Use models configured via environment variables

## Environment Variable Model Configuration

When using `MODEL_SOURCE=envars`, you must configure at least one chat model, one embedding model, and one image caption model.

### Required Chat Model Configuration
```bash
CHAT_MODEL_DEPLOYMENT_NAME=gpt-4o
CHAT_MODEL_ENDPOINT=https://your-openai-resource.openai.azure.com/
CHAT_MODEL_API_KEY=your_api_key
CHAT_MODEL_VERSION=1  # Optional, defaults to "1"
```

### Required Text Embedding Model Configuration
```bash
EMBEDDING_MODEL_DEPLOYMENT_NAME=text-embedding-3-small
EMBEDDING_MODEL_ENDPOINT=https://your-openai-resource.openai.azure.com/
EMBEDDING_MODEL_API_KEY=your_api_key
EMBEDDING_MODEL_VERSION=1  # Optional, defaults to "1"
```

### Required Image Captioning Model Configuration
```bash
IMAGE_CAPTION_MODEL_DEPLOYMENT_NAME=gpt-4o
IMAGE_CAPTION_MODEL_ENDPOINT=https://your-openai-resource.openai.azure.com/
IMAGE_CAPTION_MODEL_API_KEY=your_api_key
IMAGE_CAPTION_MODEL_VERSION=1  # Optional, defaults to "1"
```

### Optional Image Embedding Model Configuration
```bash
CLIP_MODEL_DEPLOYMENT_NAME=openai-clip-image-text-embedd-3
CLIP_MODEL_ENDPOINT=https://your-ml-endpoint.azureml.net/
CLIP_MODEL_ENDPOINT_API_KEY=your_api_key
IMAGE_EMBEDDING_MODEL_VERSION=3  # Optional, defaults to "3"
```

## Supported Models

The application validates that deployment names match supported models defined in `ModelSpecifications.cs`:

### Chat Models
- `gpt-4`
- `gpt-4o`
- `o1`
- `o1-mini`

### Text Embedding Models
- `text-embedding-ada-002` (1536 dimensions)
- `text-embedding-3-small` (1536 dimensions)
- `text-embedding-3-large` (3072 dimensions)

### Image Embedding Models
- `openai-clip-image-text-embedd-3` (768 dimensions)

### Image Captioning Models
- `gpt-4o`

## Other Required Environment Variables

### PostgreSQL Configuration
```bash
PG_HOST=localhost
PG_DATABASE=your_database_name
PG_USER=your_username
PG_PASSWORD=your_password
PG_PORT=5432  # Optional, defaults to 5432
```

### Azure Configuration (when MODEL_SOURCE=azure)
```bash
MODEL_SOURCE=envars
AZURE_SUBSCRIPTION_ID=your_azure_subscription_id
```

### Application Configuration
```bash
PORT=5272  # Optional, port for the application to listen on, defaults to 5272
SSL_VERIFY=True  # Set to False to disable SSL verification (not recommended for production)
```

### Blob Storage Configuration
```bash
BLOB_STORAGE_CONNECTION_STRING=your_blob_storage_connection_string
BLOB_STORAGE_RAG_CONTAINER_NAME=your_container_name
```

### Processing Configuration
```bash
MAX_RESURSIVE_SPLIT_DEPTH=100  # Optional, maximum depth for recursive text splitting
CHUNKING_PARALLELISM=5  # Optional, parallelism level for text chunking operations
```

### CLIP Model Configuration (Alternative image embedding)
```bash
CLIP_MODEL_DEPLOYMENT_NAME=openai-clip-image-text-embedd-3
CLIP_MODEL_ENDPOINT=https://your-ml-endpoint.azureml.net/
CLIP_MODEL_ENDPOINT_API_KEY=your_api_key
```

## Example .env File

```bash
# Model source
MODEL_SOURCE=envars || subscription
PROVIDER=azure || bedrock
AZURE_SUBSCRIPTION_ID=your_azure_subscription_id

# Application configuration
PORT=5272

# PostgreSQL
PG_HOST=localhost
PG_DATABASE=vector_db
PG_USER=postgres
PG_PASSWORD=your_password
PG_PORT=5432

# Blob Storage
BLOB_STORAGE_CONNECTION_STRING=DefaultEndpointsProtocol=https;AccountName=your_account;AccountKey=your_key;EndpointSuffix=core.windows.net
BLOB_STORAGE_RAG_CONTAINER_NAME=dynamicrag

# Processing configuration
MAX_RESURSIVE_SPLIT_DEPTH=100
CHUNKING_PARALLELISM=5

# Required models (when MODEL_SOURCE=envars)
CHAT_MODEL_DEPLOYMENT_NAME=gpt-4o
CHAT_MODEL_ENDPOINT=https://your-openai-resource.openai.azure.com/
CHAT_MODEL_API_KEY=your_api_key

EMBEDDING_MODEL_DEPLOYMENT_NAME=text-embedding-3-small
EMBEDDING_MODEL_ENDPOINT=https://your-openai-resource.openai.azure.com/
EMBEDDING_MODEL_API_KEY=your_api_key

IMAGE_CAPTION_MODEL_DEPLOYMENT_NAME=gpt-4o
IMAGE_CAPTION_MODEL_ENDPOINT=https://your-openai-resource.openai.azure.com/
IMAGE_CAPTION_MODEL_API_KEY=your_api_key

# CLIP model (alternative image embedding)
CLIP_MODEL_DEPLOYMENT_NAME=openai-clip-image-text-embedd-3
CLIP_MODEL_ENDPOINT=https://your-ml-endpoint.azureml.net/
CLIP_MODEL_ENDPOINT_API_KEY=your_api_key

# SSL configuration
SSL_VERIFY=True
``` 