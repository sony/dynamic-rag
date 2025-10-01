import {
  CreateCollectionRequest,
  IngestFileRequest,
  RemoveFileRequest,
  SearchRequest,
  ChatRequest,
  ContextAwareChatResponse,
  EmbeddingSearchResult,
  DownloadBlobRequest
} from './types';
import { API_ENDPOINTS, MOCK_CHAT_MODELS, MOCK_EMBEDDING_MODELS, MOCK_COLLECTION_RESPONSES } from './constants';
// import { generateRandomId } from './utils';

type RuntimeConfig = { 
  VITE_API_URL: string, 
  VITE_OCP_APIM_SUBSCRIPTION_KEY?: string 
};

// Storage for runtime configuration values
let runtimeBaseUrl: string | null = null;
let runtimeSubscriptionKey: string | null = null;

// Build-time fallbacks (used only if runtime config fails)
const buildTimeBaseUrl = import.meta.env.VITE_API_URL || 'http://localhost:5272';
const buildTimeSubscriptionKey = import.meta.env.VITE_OCP_APIM_SUBSCRIPTION_KEY || null;

// Initialize a promise to track when config is loaded
const configLoadedPromise = new Promise<void>((resolve) => {
  // As soon as this file is loaded in the browser, fetch the runtime config
  fetch('/api/config')
    .then(res => {
      if (!res.ok) throw new Error(`Config fetch failed (${res.status})`);
      return res.json() as Promise<RuntimeConfig>;
    })
    .then(cfg => {
      // console.log("Got the config when starting api.ts: ", cfg);
      // Update runtime values from server config
      if (cfg.VITE_API_URL) runtimeBaseUrl = cfg.VITE_API_URL;
      if (cfg.VITE_OCP_APIM_SUBSCRIPTION_KEY) runtimeSubscriptionKey = cfg.VITE_OCP_APIM_SUBSCRIPTION_KEY;
      resolve();
    })
    .catch((error) => {
      console.warn('Could not load runtime config, falling back to build-time env:', error);
      resolve(); // Resolve anyway so API calls can proceed with fallbacks
    });
});

// Getters for configuration values
export const getBaseUrl = (): string => {
  return runtimeBaseUrl ?? buildTimeBaseUrl;
};

export const getSubscriptionKey = (): string | null => {
  return runtimeSubscriptionKey ?? buildTimeSubscriptionKey;
};
// // Define the base URL for API calls
// const getBaseUrl = () => {
//   console.log('Environment Variables:', import.meta.env);
//   const prodUrl = import.meta.env.VITE_API_URL || 'http://localhost:5272';
//   console.log(prodUrl);
//   return prodUrl;
// };

/**
 * Makes a request to the API in production mode
 */
async function makeApiRequest<T>(
  endpoint: string,
  method: 'GET' | 'POST' = 'GET',
  data?: unknown,
  queryParams?: Record<string, any>
): Promise<T> {
  // Wait for config to be loaded before proceeding
  await configLoadedPromise;
  const url = new URL(`${getBaseUrl()}${endpoint}`);
  
  // Add query parameters if they exist
  if (queryParams) {
    Object.entries(queryParams).forEach(([key, value]) => {
      if (value !== undefined && value !== null) {
        url.searchParams.append(key, String(value));
      }
    });
  }

  // Prepare headers
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  };

  // Add Ocp-Apim-Subscription-Key header if available
  const currentSubscriptionKey = getSubscriptionKey();
  if (currentSubscriptionKey) {
    headers['Ocp-Apim-Subscription-Key'] = currentSubscriptionKey;
  }

  const response = await fetch(url.toString(), {
    method,
    headers,
    body: data ? JSON.stringify(data) : undefined,
  });

  if (!response.ok) {
    const errorText = await response.text();
    throw new Error(`API error (${response.status}): ${errorText || response.statusText}`);
  }

  return await response.json();
}

// Helper function for making API requests with FormData
export async function makeApiRequestWithFormData(
  endpoint: string,
  formData: FormData
): Promise<any> {
  // Wait for config to be loaded before proceeding
  await configLoadedPromise;
  const url = new URL(`${getBaseUrl()}${endpoint}`);

  try {
    // Prepare headers (don't set Content-Type - browser will set it with boundary)
    const headers: Record<string, string> = {};
    
    // Add Ocp-Apim-Subscription-Key header if available
    const currentSubscriptionKey = getSubscriptionKey();
    if (currentSubscriptionKey) {
      headers['Ocp-Apim-Subscription-Key'] = currentSubscriptionKey;
    }
    
    const response = await fetch(url.toString(), {
      method: 'POST',
      headers,
      body: formData,
    });

    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}));
      throw new Error(errorData.message || `API request failed with status ${response.status}`);
    }

    return response.json();
  } catch (error) {
    console.error('API request error:', error);
    throw error;
  }
}

/**
 * API client that either makes real API calls or returns mock data based on environment mode
 */
export class ApiClient {
  private isDevelopmentMode: boolean;

  constructor(isDevelopmentMode: boolean) {
    this.isDevelopmentMode = isDevelopmentMode;
    console.log(`API Client initialized in ${isDevelopmentMode ? 'development' : 'production'} mode`);
  }

  // Download blob
  async downloadSingleBlob(data: DownloadBlobRequest): Promise<any> {
    if (this.isDevelopmentMode) {
      // Mock response
      return {
        success: true,
        blob: new Blob(['Mock file content'], { type: 'text/plain' }),
        fileName: `mock_file.txt`
      };
    }
    return makeApiRequest(API_ENDPOINTS.DOWNLOAD_BLOB, 'POST', data, undefined);
  }

  async removeFile(data: RemoveFileRequest): Promise<any> {
    if (this.isDevelopmentMode) {
      // Mock response
      return { success: true, message: `File "${data.filePath}" removed from "${data.collectionName}" successfully` };
    }
    console.log("remove file request");
    console.log(data);
    return makeApiRequest(API_ENDPOINTS.REMOVE_FILE, 'POST', data);
  }

  // Schema Management API
  async createCollection(data: CreateCollectionRequest): Promise<any> {
    if (this.isDevelopmentMode) {
      // Mock response
      return { success: true, message: `Collection "${data.collectionName}" created successfully` };
    }
    return makeApiRequest(API_ENDPOINTS.CREATE_COLLECTION, 'POST', data, undefined);
  }

  async deleteCollection(collectionName: string): Promise<any> {
    if (this.isDevelopmentMode) {
      // Mock response
      return { success: true, message: `Collection "${collectionName}" deleted successfully` };
    }
    return makeApiRequest(API_ENDPOINTS.DELETE_COLLECTION, 'POST', { collectionName }, undefined);
  }

  async getCollections(): Promise<any> {
    if (this.isDevelopmentMode) {
      // Mock response with the new format
      return {
        collections: [
          {
            collection_name: 'documents',
            collection_type: 'TextEmbedding',
            default_embedding_service_id: 'text-embedding-ada-002',
            default_distance_metric: 'cosine'
          },
          {
            collection_name: 'research',
            collection_type: 'TextEmbedding',
            default_embedding_service_id: 'text-embedding-3-small',
            default_distance_metric: 'euclidean'
          }
        ]
      };
    }
    return makeApiRequest<any>(API_ENDPOINTS.READ_COLLECTIONS);
  }

  async getCollectionFiles(): Promise<Record<string, object>> {
    if (this.isDevelopmentMode) {
      // Mock response
      return { "collections" : 
                {
                  "documents" : [ 
                    {
                      "filename": "testdir1",
                      "pathInContainer": "documents/documents.txt",
                      "sizeMB": 0.0,
                      "lastModified": "2025-04-04T15:47:38+00:00",
                      "contentType": "image/png"
                    }
                  ],
                  "research" : [ 
                    {
                      "filename": "testdir1",
                      "pathInContainer": "research/research.txt",
                      "sizeMB": 0.0,
                      "lastModified": "2025-04-04T15:47:38+00:00",
                      "contentType": "image/png"
                    }
                  ],
                }
              }
    }
    return makeApiRequest<Record<string, object>>(API_ENDPOINTS.READ_ALL_BLOBS);
  }

  async getChatModels(): Promise<string[]> {
    if (this.isDevelopmentMode) {
      // Mock response
      return MOCK_CHAT_MODELS;
    }
    return makeApiRequest<string[]>(API_ENDPOINTS.READ_CHAT_MODELS);
  }

  async getEmbeddingModels(): Promise<{ ImageEmbedding: string[], TextEmbedding: string[] }> {
    if (this.isDevelopmentMode) {
      // Mock response with the new structure
      return {
        ImageEmbedding: ['openai-clip-image-text-embedd-3'],
        TextEmbedding: MOCK_EMBEDDING_MODELS
      };
    }
    const response = await makeApiRequest<{ embedding_models: { ImageEmbedding: string[], TextEmbedding: string[] } }>(API_ENDPOINTS.READ_EMBEDDING_MODELS);
    return response.embedding_models;
  }

  // // Collection Management API
  // async ingestFile(data: IngestFileRequest): Promise<any> {
  //   if (this.isDevelopmentMode) {
  //     // Mock response
  //     return { success: true, message: `File "${data.file?.name}" ingested into "${data.collectionName}" successfully` };
  //   }
  //   return makeApiRequest(API_ENDPOINTS.INGEST_FILE, 'POST', data);
  // }

    async ingestFile(data: IngestFileRequest & { file: File }): Promise<any> {
      console.log("ingestFile");
      if (this.isDevelopmentMode) {
        // Mock response
        return { success: true, message: `File "${data.file?.name}" ingested into "${data.collectionName}" successfully` };
      }
      
      // Create FormData for file upload
      const formData = new FormData();
      
      // Append the file
      if (data.file) {
        formData.append('file', data.file);
      }
      
      // Append other fields
      formData.append('collectionName', data.collectionName);
      console.log("data");
      console.log(data);
      formData.append('chunkSize', data.chunkSize.toString());
      console.log("ingestFile");
      formData.append('chunkOverlapFraction', data.chunkOverlapFraction.toString());
      
      if (data.saveToBlobStorage !== undefined) {
        formData.append('saveToBlobStorage', data.saveToBlobStorage.toString());
      }
      
      // If localFilePath is provided, include it
      if (data.filePath) {
        formData.append('filePath', data.filePath);
      }

      console.log("Ingest file request");
      console.log(formData);
      
      // Use a modified makeApiRequest that handles FormData
      return makeApiRequestWithFormData(API_ENDPOINTS.INGEST_FILE, formData);
    }

  // async vectorSearch(data: SearchRequest): Promise<EmbeddingSearchResult[]> {
  //   if (this.isDevelopmentMode) {
  //     // Mock response
  //     return this.getMockSearchResults(data.collectionName, data.nResults);
  //   }
  //   return makeApiRequest<EmbeddingSearchResult[]>(API_ENDPOINTS.VECTOR_SEARCH, 'POST', data);
  // }

  // async semanticSearch(data: SearchRequest): Promise<EmbeddingSearchResult[]> {
  //   if (this.isDevelopmentMode) {
  //     // Mock response
  //     return this.getMockSearchResults(data.collectionName, data.nResults);
  //   }
  //   return makeApiRequest<EmbeddingSearchResult[]>(API_ENDPOINTS.SEMANTIC_SEARCH, 'POST', data);
  // }

  // async hybridSearch(data: SearchRequest): Promise<EmbeddingSearchResult[]> {
  //   if (this.isDevelopmentMode) {
  //     // Mock response
  //     return this.getMockSearchResults(data.collectionName, data.nResults);
  //   }
  //   return makeApiRequest<EmbeddingSearchResult[]>(API_ENDPOINTS.HYBRID_SEARCH, 'POST', data);
  // }

  async imageSearch(data: SearchRequest): Promise<EmbeddingSearchResult[]> {
    if (this.isDevelopmentMode) {
      // Mock response
      return this.getMockSearchResults(data.collectionName, data.nResults);
    }
    return makeApiRequest<EmbeddingSearchResult[]>(API_ENDPOINTS.IMAGE_SEARCH, 'POST', data);
  }

  /**
   * Performs a text-based search on an image collection
   * @param query Text query to search for images
   * @param collectionName Name of the image collection
   * @param nResults Number of results to return
   * @param timeWindowEnabled Whether to enable time-window filtering
   * @param windowDays Number of days to include in the time window
   * @param temporalDecayEnabled Whether to apply temporal decay based on timestamp
   * @param decayImpact Decay impact factor for exponential decay
   * @returns Search results with image data
   */
  async textSearchImage(query: string, collectionName: string, nResults: number, timeWindowEnabled: boolean = false, windowDays: number = 30, temporalDecayEnabled: boolean = false, decayImpact: number = 0.01): Promise<{search_results: EmbeddingSearchResult[], final_answer: string}> {
    if (this.isDevelopmentMode) {
      // Mock response
      return {
        search_results: this.getMockSearchResults(collectionName, nResults),
        final_answer: "Images matching your text query..."
      };
    }
    
    const data = {
      query,
      collectionName,
      nResults,
      timeWindowEnabled,
      windowDays,
      temporalDecayEnabled,
      decayImpact
    };
    
    const response = await makeApiRequest(API_ENDPOINTS.TEXT_SEARCH, 'POST', data) as {search_results: EmbeddingSearchResult[], internal_execution_time: number, success: boolean};
    return {
      search_results: response.search_results,
      final_answer: "See image results below"
    };
  }

  async imageSearchWithImage(image: File, collectionName: string, nResults: number, timeWindowEnabled: boolean = false, windowDays: number = 30, temporalDecayEnabled: boolean = false, decayImpact: number = 0.01): Promise<{search_results: EmbeddingSearchResult[], final_answer: string}> {
    if (this.isDevelopmentMode) {
      // Mock response
      return {
        search_results: this.getMockSearchResults(collectionName, nResults),
        final_answer: "Similar images..."
      };
    }
    
    const formData = new FormData();
    formData.append('image', image);
    formData.append('collectionName', collectionName);
    formData.append('nResults', nResults.toString());
    formData.append('timeWindowEnabled', timeWindowEnabled.toString());
    formData.append('windowDays', windowDays.toString());
    formData.append('temporalDecayEnabled', temporalDecayEnabled.toString());
    formData.append('decayImpact', decayImpact.toString());
    
    const response = await makeApiRequestWithFormData(API_ENDPOINTS.IMAGE_SEARCH, formData) as {search_results: EmbeddingSearchResult[], internal_execution_time: number, success: boolean};
    return {
      search_results: response.search_results,
      final_answer: "See citations below"
    };
  }

  // Context-Aware Chat API
  async chatWithCollection(data: ChatRequest): Promise<ContextAwareChatResponse> {
    if (this.isDevelopmentMode) {
      // Mock response
      return this.getMockChatResponse(data);
    }
    return makeApiRequest<ContextAwareChatResponse>(API_ENDPOINTS.CHAT, 'POST', data);
  }

  // Helper methods for mock responses
  private getMockSearchResults(collectionName: string, nResults: number): EmbeddingSearchResult[] {
    // Generate mock search results
    const results: EmbeddingSearchResult[] = [];
    const fileNames = collectionName === 'documents' 
      ? ['report.pdf', 'data.csv'] 
      : ['paper.docx'];
      
    for (let i = 0; i < Math.min(nResults, 5); i++) {
      const fileName = fileNames[i % fileNames.length];
      results.push({
        rank: i,
        score: (0.95 - (i * 0.1)).toString(),
        fileName,
        chunkIndex: i + 1,
        definition: `This is chunk ${i + 1} from ${fileName} containing relevant information about the search query.`,
        embeddingModelName: 'text-embedding-ada-002'
      });
    }
    
    return results;
  }

  private getMockChatResponse(data: ChatRequest): ContextAwareChatResponse {
    // Get mock response based on collection
    const mockData = MOCK_COLLECTION_RESPONSES[data.collectionName as keyof typeof MOCK_COLLECTION_RESPONSES] || 
                    MOCK_COLLECTION_RESPONSES.documents;
      
    // If the query contains certain keywords, customize the response
    let finalAnswer = mockData.response;
    let sources = mockData.sources;
    
    if (data.query.toLowerCase().includes('summary') || data.query.toLowerCase().includes('summarize')) {
      finalAnswer = "Based on the content in this collection, the documents primarily focus on data processing algorithms and performance optimization techniques. There's detailed analysis of processing efficiency, memory usage, and scaling characteristics across different workloads.";
      sources = data.collectionName === 'documents' 
        ? "report.pdf (chunks 1-4), data.csv (chunks 2-3)" 
        : "paper.docx (chunks 1-5)";
    } else if (data.query.toLowerCase().includes('compare') || data.query.toLowerCase().includes('difference')) {
      finalAnswer = "The analysis shows a significant difference in performance between the traditional and optimized approaches. The optimized algorithm demonstrates a 27% improvement in processing efficiency, particularly for large datasets, while maintaining accuracy within acceptable parameters.";
      sources = data.collectionName === 'documents' 
        ? "report.pdf (chunks 6-8), data.csv (chunk 5)" 
        : "paper.docx (chunks 7-9)";
    }
    
    return {
      query: data.query,
      finalAnswer,
      searchResult: this.getMockSearchResults(data.collectionName, data.nResults)
    };
  }
}
