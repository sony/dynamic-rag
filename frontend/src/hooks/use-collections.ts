import { useQuery } from '@tanstack/react-query';
import { useApi } from './use-api';

// Import the FileInfo type directly
export interface FileInfo {
  filename: string;
  pathInContainer: string;
  sizeMB: number;
  lastModified: string;
  contentType: string;
}

export interface CollectionWithFiles {
  name: string;
  files: FileInfo[];
  embeddingModel: string;
  distanceMetric: string;
  collectionType?: string;
}

/**
 * Hook to fetch collections and their associated files
 */
export function useCollections() {
  const { apiClient } = useApi();

  const { data: collectionsWithFiles = [], isLoading, error, refetch } = useQuery({
    queryKey: ['collections-with-files'],
    queryFn: async () => {
      const collectionsResponse = await apiClient.getCollections();
      
      // Get all files for all collections
      const filesResponse = await apiClient.getCollectionFiles();
      const filesData = filesResponse.collections || {};
      
      // Map collections to include their files and other metadata
      return collectionsResponse.collections.map((collection: any) => {
        const collectionName = collection.collection_name;
        const files = filesData[collectionName] || [];
        return { 
          name: collectionName, 
          files: files as FileInfo[],
          description: collection.description,
          embeddingModel: collection.default_embedding_service_id,
          distanceMetric: collection.default_distance_metric,
          collectionType: collection.collection_type
        };
      });
    }
  });

  return {
    collections: collectionsWithFiles,
    isLoading,
    error,
    refetch
  };
}
