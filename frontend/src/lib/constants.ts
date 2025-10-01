// API endpoints
export const API_ENDPOINTS = {
  // Blob management
  READ_ALL_BLOBS: '/StorageManagement/read-all-blobs',
  // DELETE_COLLECTION_BLOBS: '/StorageManagement/delete-collection-blobs', // Tied to delete collection no need to route this in this program
  // DELETE_SINGLE_BLOB: '/StorageManagement/delete-single-blob',
  // UPLOAD_SINGLE_BLOB: '/StorageManagement/upload-single-blob',
  DOWNLOAD_BLOB: '/StorageManagement/download-single-blob',

  // Collection Management
  CREATE_COLLECTION: '/CollectionManagement/create-collection',
  DELETE_COLLECTION: '/CollectionManagement/delete-collection',
  INGEST_FILE: '/CollectionManagement/ingest-file',
  REMOVE_FILE: '/CollectionManagement/remove-file-from-collection',
  // VECTOR_SEARCH: '/CollectionManagement/vector-search-collection',
  // SEMANTIC_SEARCH: '/CollectionManagement/semantic-search-collection',
  // HYBRID_SEARCH: '/CollectionManagement/hybrid-search-collection',
  IMAGE_SEARCH: '/CollectionManagement/image-search-collection',
  TEXT_SEARCH: '/CollectionManagement/text-search-collection',
  
  // Context-Aware Chat
  CHAT: '/ContextAwareChat/chat-with-collection',
  
  // Schema Management
  READ_CHAT_MODELS: '/SchemaManagement/read-chat-models',
  READ_EMBEDDING_MODELS: '/SchemaManagement/read-embedding-models',
  READ_COLLECTIONS: '/SchemaManagement/read-collections',
};

// File types supported by the system
export const SUPPORTED_FILE_TYPES = [
  'application/json',                     // JSON
  'application/xml', 'text/xml',          // XML
  'text/csv',                             // CSV
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document', // DOCX
  'application/pdf',                      // PDF
  'text/plain',                           // TXT
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', // XLSX
  'application/vnd.ms-excel',             // XLS
  'image/png',                            // PNG
  'image/jpeg',                           // JPEG
  'image/bmp',                            // BMP
];

// Mock data for development mode
export const MOCK_CHAT_MODELS = [
  'gpt-3.5-turbo',
  'gpt-4'
];

export const MOCK_EMBEDDING_MODELS = [
  'text-embedding-ada-002',
  'text-embedding-3-small',
  'text-embedding-3-large'
];

export const MOCK_COLLECTION_RESPONSES = {
  documents: {
    query: "Tell me about the documents in this collection",
    response: "Based on the documents in this collection, I can see there are two main files: 'report.pdf' and 'data.csv'. The report appears to contain information about a data analysis project, including methodology and findings. The CSV file contains structured data that was likely used in the analysis mentioned in the report.",
    sources: "report.pdf (chunk 2), data.csv (chunk 1)"
  },
  research: {
    query: "What's in this research collection?", 
    response: "The research collection contains a single document called 'paper.docx'. This appears to be an academic paper discussing advanced algorithms for natural language processing with a focus on transformer architecture improvements. The paper includes several experimental results and performance benchmarks.",
    sources: "paper.docx (chunks 1-3)"
  }
};
