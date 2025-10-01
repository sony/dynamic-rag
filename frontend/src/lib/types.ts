// Collection types
export interface Collection {
  name: string;
  files: File[];
  embeddingModel: string;
  collectionType?: string;
}

export interface File {
  name: string;
  path: string;
  size?: number;
  type?: string;
  lastModified?: Date;
}

export interface CreateCollectionRequest {
  defaultEmbeddingModelName: string;
  collectionName: string;
  description?: string;
  initHNSW: boolean;
  initBTree: boolean;
  embeddingType?: string;
}

export interface DownloadBlobRequest {
  pathInContainer: string
}

// Chat types
export interface Message {
  id: string;
  role: 'user' | 'system';
  content: string;
  sources?: string;
  searchResults?: EmbeddingSearchResult[];
  timestamp: Date;
}

export interface ChatRequest {
  query: string;
  collectionName: string;
  nResults: number;
  chatModelName: string;
  timeWindowEnabled: boolean;
  windowDays: number;
  temporalDecayEnabled: boolean;
  decayImpact: number;
}

// Search result types
export interface EmbeddingSearchResult {
  rank: number;
  score: string;
  fileName: string;
  chunkIndex: number;
  definition: string;
  embeddingModelName: string;
  imageData?: string; // Base64 encoded image data
  timestamp?: string;
}

export interface ContextAwareChatResponse {
  searchResult: EmbeddingSearchResult[];
  query: string;
  finalAnswer: string;
}

// // File management types
// export interface IngestFileRequest {
//   filePath: string;
//   collectionName: string;
//   chunkSize: number;
//   chunkOverlapFraction: number;
// }

// File management types
export interface IngestFileRequest {
  filePath?: string;            // Optional path to local file on server
  file: File;            // Optional file object for direct uploads
  collectionName: string;       // Required collection name
  chunkSize: number;            // Chunk size for embedding
  chunkOverlapFraction: number; // Overlap fraction between chunks
  saveToBlobStorage?: boolean;  // Whether to save a copy to blob storage
}

export interface RemoveFileRequest {
  pathInContainer: string;
  collectionName: string;
  isBlobFile: boolean
}

// Search types
export interface SearchRequest {
  query: string;
  collectionName: string;
  nResults: number;
}

// Modal types
export interface ConfirmDialogProps {
  isOpen: boolean;
  title: string;
  message: string;
  confirmText?: string;
  cancelText?: string;
  onConfirm: () => void;
  onCancel: () => void;
}

export interface EmbeddingModelsResponse {
  embedding_models: {
    ImageEmbedding: string[];
    TextEmbedding: string[];
  };
  internal_execution_time: number;
}

export interface NewCollectionModalProps {
  isOpen: boolean;
  embeddingModels: {
    ImageEmbedding: string[];
    TextEmbedding: string[];
  };
  onSubmit: (data: CreateCollectionRequest & { embeddingType: string }) => void;
  onClose: () => void;
}

export interface FileUploadModalProps {
  isOpen: boolean;
  collectionName: string;
  onSubmit: (data: IngestFileRequest & { file: File } ) => void;
  onClose: () => void;
}
