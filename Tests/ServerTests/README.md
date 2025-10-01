# Server Integration Tests

This directory contains comprehensive integration tests for the PgVector Dynamic RAG server project, testing all 19 API endpoints across 4 main controller areas.

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0) or later installed
- macOS, Linux, or Windows
- Valid `.env` file in the test directory with required configuration
- Access to PostgreSQL database and Azure Blob Storage
- API keys for embedding and chat models

## Test Architecture

The integration tests are organized into 4 main test files that cover all API endpoints:

### 1. BlobManagementControllerTests.cs (6 tests)
Tests blob storage operations for file management.

**APIs Tested:**
- `GET /BlobManagement/read-all-blobs` - Lists all blob collections and their files
- `POST /BlobManagement/upload-single-blob` - Uploads files to blob storage
- `POST /BlobManagement/download-single-blob` - Generates secure download URLs
- `POST /BlobManagement/delete-single-blob` - Deletes individual files
- `POST /BlobManagement/delete-collection-blobs` - Deletes entire collections

**Test Flow (Priority 1-6):**
1. **Read All Blobs** - Validates blob storage structure and existing content
2. **Upload Files** - Tests multipart file uploads (DOCX, PDF, CSV, JSON, TXT)
3. **Download Files** - Generates and validates SAS download URLs
4. **Delete Individual Files** - Removes specific files from storage
5. **Delete Collections** - Bulk deletion of entire collections
6. **Cleanup** - Final cleanup of any remaining test artifacts

### 2. CollectionManagementControllerTests.cs (11 tests)
Tests vector database collection operations and search functionality.

**APIs Tested:**
- `POST /CollectionManagement/create-collection` - Creates text/image embedding collections
- `POST /CollectionManagement/ingest-file` - Ingests files into collections
- `POST /CollectionManagement/new-text-memory` - Creates text memories directly
- `POST /CollectionManagement/text-vector-search-collection` - Pure vector similarity search
- `POST /CollectionManagement/text-semantic-search-collection` - Semantic search with keywords
- `POST /CollectionManagement/text-search-collection` - Hybrid vector + semantic search
- `POST /CollectionManagement/image-search-collection` - Image-based similarity search
- `POST /CollectionManagement/remove-file-from-collection` - Removes files from collections
- `POST /CollectionManagement/update-index` - Updates collection indexes
- `POST /CollectionManagement/delete-collection` - Deletes entire collections

**Test Flow (Priority 1-11):**
1. **Create Collections** - Creates both TextEmbedding and ImageEmbedding collections
2. **Ingest Files** - Tests file ingestion with chunking and embedding
3. **Create Text Memories** - Tests direct text content ingestion
4. **Vector Search** - Tests similarity search across both collection types
5. **Semantic Search** - Tests keyword-based search (TextEmbedding only)
6. **Hybrid Search** - Tests combined vector + semantic search with alpha weighting
7. **Image Search** - Tests image-based search (ImageEmbedding collections only)
8. **Remove Files** - Tests selective file removal from collections
9. **Update Index** - Tests index optimization and updates
10. **Delete Collections** - Tests complete collection deletion
11. **Final Cleanup** - Removes any remaining test collections

### 3. ContextAwareChatControllerTests.cs (2 tests)
Tests AI chat functionality with RAG (Retrieval-Augmented Generation).

**APIs Tested:**
- `POST /ContextAwareChat/chat-with-collection` - AI chat with collection context

**Test Scenarios:**
- **Basic Chat** - Simple questions about collection content
- **Specific Questions** - Targeted queries with result filtering
- **Time Window Chat** - Chat with temporal filtering and decay
- **Error Handling** - Invalid collections and model names

### 4. SchemaManagementControllerTests.cs (4 tests)
Tests system configuration and metadata retrieval.

**APIs Tested:**
- `GET /SchemaManagement/read-chat-models` - Lists available chat models
- `GET /SchemaManagement/read-embedding-models` - Lists embedding models by modality
- `GET /SchemaManagement/read-collections` - Lists all vector collections

**Test Coverage:**
- **Chat Models** - Validates available AI chat models (GPT-4, Claude, etc.)
- **Embedding Models** - Validates text and image embedding models
- **Collections** - Validates existing vector database collections

## Running the Tests

### Run All Integration Tests
```sh
cd Tests/ServerTests
dotnet test
```

### Run Individual Test Files

**Storage Management Tests:**
```sh
dotnet test --filter "StorageManagementControllerTests"
```

**Blob Management Tests:**
```sh
dotnet test --filter "BlobManagementServiceTests"
```

**S3 Management Tests:**
```sh
dotnet test --filter "S3ManagementServiceTests"
```

**Collection Management Tests:**
```sh
dotnet test --filter "CollectionManagementControllerTests"
```

**Chat Controller Tests:**
```sh
dotnet test --filter "ContextAwareChatControllerTests"
```

**Schema Management Tests:**
```sh
dotnet test --filter "SchemaManagementControllerTests"
```

### Run Specific Test Methods

**Single API Test:**
```sh
dotnet test --filter "GET_ReadAllBlobs_ReturnsOkWithBlobCollections"
```

**Multiple Related Tests:**
```sh
dotnet test --filter "POST_UploadSingleBlob_ReturnsOkWithUploadDetails|POST_DownloadSingleBlob_ReturnsOkWithDownloadDetails"
```

### Verbose vs Clean Mode

**Clean Mode:**
```sh
TEST_VERBOSE=false dotnet test
```
- Suppresses ASP.NET Core framework logs
- Shows only test results and key information
- Best for CI/CD and general development

**Verbose Mode:**
```sh
TEST_VERBOSE=true dotnet test
```
- Shows full ASP.NET Core logs
- Displays complete HTTP request/response details
- Useful for debugging and development

### Run with Coverage
```sh
dotnet test --collect:"XPlat Code Coverage"
```

## Test Priority System

The tests use `Xunit.Priority` to ensure proper execution order:

### BlobManagementController (Priority 1-6)
- **Priority 1:** Read initial state
- **Priority 2:** Upload test files
- **Priority 3:** Download files (depends on upload)
- **Priority 4:** Delete individual files (depends on upload)
- **Priority 5:** Delete collections (depends on upload)
- **Priority 6:** Final cleanup

### CollectionManagementController (Priority 1-11)
- **Priority 1:** Create collections
- **Priority 2:** Ingest files (depends on collections)
- **Priority 3:** Create text memories (depends on collections)
- **Priority 4-7:** Search operations (depend on ingested content)
- **Priority 8:** Remove files (depends on ingested content)
- **Priority 9:** Update indexes (depends on collections)
- **Priority 10:** Delete collections
- **Priority 11:** Final cleanup

### Other Controllers
- **ContextAwareChat:** No priority dependencies (uses existing collections)
- **SchemaManagement:** No priority dependencies (read-only operations)

## Test Data Requirements

### Required Test Files (TestData folder)
```
TestData/
├── test_file.docx     # Microsoft Word document
├── test_file.pdf      # PDF document
├── test_file.csv      # CSV data file
├── test_file.json     # JSON data file
├── test_file.txt      # Plain text file
├── test_file.jpg      # JPEG image file
└── test_image.jpg     # Additional test image
```

### Environment Configuration (.env)
```env
# Database Configuration
POSTGRES_CONNECTION_STRING=your_postgres_connection

# Azure Blob Storage
AZURE_STORAGE_CONNECTION_STRING=your_azure_connection

# AI Model API Keys
OPENAI_API_KEY=your_openai_key
ANTHROPIC_API_KEY=your_anthropic_key

# Test Configuration
TEST_VERBOSE=false
```

## API Coverage Summary

| Controller | Endpoints | Test Methods | Key Features |
|------------|-----------|--------------|--------------|
| **BlobManagement** | 5 APIs | 6 tests | File upload/download, SAS URLs, collection management |
| **CollectionManagement** | 10 APIs | 11 tests | Vector collections, embeddings, search, RAG |
| **ContextAwareChat** | 1 API | 2 tests | AI chat with retrieval augmentation |
| **SchemaManagement** | 3 APIs | 3 tests | System metadata and configuration |
| **Total** | **19 APIs** | **22 tests** | Complete integration coverage |

## Error Handling & Validation

The tests include comprehensive validation for:

- **HTTP Status Codes** - Validates expected 200 OK and error responses
- **Content Types** - Ensures proper JSON content-type headers
- **Response Structure** - Validates required fields and data types
- **Data Quality** - Checks field values, ranges, and business rules
- **Performance** - Validates reasonable execution times
- **Error Scenarios** - Tests invalid inputs and edge cases

## Troubleshooting

### Common Issues

**Missing Test Files:**
```sh
# Tests will skip gracefully if files don't exist
echo "Create TestData folder with required files"
```

**Database Connection:**
```sh
# Verify PostgreSQL connection
echo "Check POSTGRES_CONNECTION_STRING in .env"
```

**Storage Access:**
```sh
# Verify Azure Blob Storage access
echo "Check AZURE_STORAGE_CONNECTION_STRING in .env"
```

**API Key Issues:**
```sh
# Verify AI model access
echo "Check OPENAI_API_KEY and other model keys in .env"
```

### Debug Individual Tests
```sh
# Run single test with maximum verbosity
TEST_VERBOSE=true dotnet test --filter "TestMethodName" --logger "console;verbosity=detailed"
```

## Integration with CI/CD

The tests are designed for automated environments:

- **Clean Mode Default** - Minimal log output for CI systems
- **Self-Contained** - Creates and cleans up all test data
- **Priority Ordering** - Ensures reliable execution sequence
- **Error Isolation** - Failed tests don't affect subsequent tests
- **Environment Flexible** - Works with different database/storage configs

---

For more information, see the main [README.md](../../README.md) or the server [README.md](../../server/README.md).
