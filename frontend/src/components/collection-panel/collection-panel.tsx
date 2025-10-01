import { FC, useState, useEffect } from 'react';
import { ChevronLeft, ChevronRight, FolderClosed, Trash2, Plus, X, ChevronDown } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { useToast } from '@/hooks/use-toast';
import { useApi } from '@/hooks/use-api';
import { Collection, File } from '@/lib/types';
import { IngestFileRequest } from './types';
import NewCollectionModal from './new-collection-modal';
import FileUploadModal from './file-upload-modal';
import ConfirmDialog from '@/components/ui/confirm-dialog';
import { getFileIconByExtension } from '@/lib/utils';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import type { FileInfo } from './file-item'; // Import FileInfo as a type
import FileList from './file-list';

// // Define interfaces for the file structure
// export interface FileInfo {
//   filename: string;
//   pathInContainer: string;
//   sizeMB: number;
//   lastModified: string;
//   contentType: string;
// }

export interface CollectionWithFiles {
  name: string;
  files: FileInfo[];
  description?: string;
  embeddingModel: string;
  collectionType?: string;
}


interface CollectionPanelProps {
  onSelectCollection: (name: string) => void;
  isExpanded: boolean;
  onToggleSidebar: () => void;
  collections?: CollectionWithFiles[]; // Make this optional to maintain backward compatibility
  onCollectionsChange?: () => void; // Callback to refresh collections data
}

const CollectionPanel: FC<CollectionPanelProps> = ({ 
  onSelectCollection, 
  isExpanded, 
  onToggleSidebar,
  collections: propCollections,
  onCollectionsChange
}) => {
  console.log('propCollections provided:', propCollections ? 'Yes' : 'No', propCollections);
  const [newCollectionModalOpen, setNewCollectionModalOpen] = useState(false);
  const [fileUploadModalOpen, setFileUploadModalOpen] = useState(false);
  const [confirmDialogOpen, setConfirmDialogOpen] = useState(false);
  const [activeCollection, setActiveCollection] = useState<string | null>(null);
  const [expandedCollections, setExpandedCollections] = useState<Set<string>>(new Set());
  const [downloadingFiles, setDownloadingFiles] = useState<Set<string>>(new Set());
  const [confirmDialogData, setConfirmDialogData] = useState({
    title: '',
    message: '',
    action: () => {},
  });
  
  const { toast } = useToast();
  const { apiClient } = useApi();
  const queryClient = useQueryClient();

  // // Fetch collections only if not provided as props
  // const { data: collections = [], isLoading } = useQuery({
  //   queryKey: ['collections'],
  //   queryFn: async () => {
  //     const response = await apiClient.getCollections();
  //     // Create collection objects with empty files arrays initially
  //     return response.collections.map((name: string) => ({ name, files: [] }));
  //     // return response.collections;
  //   }
  // });
  // Fetch collections only if not provided as props
  // Fetch collections only if not provided as props
  const { data: fetchedCollections = [], isLoading } = useQuery({
    queryKey: ['collections'],
    queryFn: async () => {
      console.log('Collections query function is executing');
      const response = await apiClient.getCollections();
      console.log('API Response for collections:', response);
      
      const mappedCollections = response.collections.map((collection: any) => {
        console.log('Collection item:', collection);
        return {
          name: collection.collection_name,
          files: [],
          description: collection.description,
          embeddingModel: collection.default_embedding_service_id,
          distanceMetric: collection.default_distance_metric,
          collectionType: collection.collection_type
        };
      });
      
      console.log('Mapped collections:', mappedCollections);
      return mappedCollections;
    },
    // Skip the query if collections are provided as props
    enabled: !propCollections
  });
  
  // Log whether the query is enabled
  console.log('Collections query enabled:', !propCollections);

  // Use prop collections if provided, otherwise use fetched collections
  const collections = propCollections || fetchedCollections;
  
  // Print collections to console for debugging
  console.log('Collections after mapping:', collections);

  // Fetch embedding models for the new collection modal
  const { data: embeddingModels = { ImageEmbedding: [], TextEmbedding: [] } } = useQuery({
    queryKey: ['embeddingModels'],
    queryFn: async () => {
      return await apiClient.getEmbeddingModels();
    }
  });

  // console.log("Embedding models:", embeddingModels);

  // download file mutation
  // const downloadFileMutation = useMutation({
  //   mutationFn: async (pathInContainer: string) => {
  //     const response = await fetch('/api/download-single-blob', {
  //       method: 'POST',
  //       headers: {
  //         'Content-Type': 'application/json',
  //       },
  //       body: JSON.stringify({ pathInContainer }),
  //     });
      
  //     if (!response.ok) {
  //       throw new Error('Failed to generate download URL');
  //     }
      
  //     return response.json();
  //   },
  //   onSuccess: (data) => {
  //     // Open the download URL in a new tab if it exists
  //     if (data.upload_details && data.upload_details.downloadUrl) {
  //       window.open(data.upload_details.downloadUrl, '_blank');
  //     } else {
  //       throw new Error('Download URL not found in response');
  //     }
  //   },
  //   onError: (error: any) => {
  //     toast({
  //       title: "Error",
  //       description: `Failed to download file: ${error.message}`,
  //       variant: "destructive",
  //     });
  //   }
  // });
  
  // const handleDownloadFile = (pathInContainer: string, filename: string) => {
  //   toast({
  //     title: "Downloading...",
  //     description: `Preparing ${filename} for download`,
  //     variant: "default",
  //   });
    
  //   downloadFileMutation.mutate(pathInContainer);
  // };

  // Create collection mutation
  const createCollectionMutation = useMutation({
    mutationFn: async (data: any) => apiClient.createCollection(data),
    onSuccess: () => {
      toast({
        title: "Success",
        description: "Collection created successfully",
        variant: "default",
      });
      queryClient.invalidateQueries({ queryKey: ['collections'] });
      // Call the onCollectionsChange callback if provided
      if (onCollectionsChange) onCollectionsChange();
      setNewCollectionModalOpen(false);
    },
    onError: (error: any) => {
      toast({
        title: "Error",
        description: `Failed to create collection: ${error.message}`,
        variant: "destructive",
      });
    }
  });

  // Delete collection mutation
  const deleteCollectionMutation = useMutation({
    mutationFn: async (collectionName: string) => apiClient.deleteCollection(collectionName),
    onSuccess: () => {
      toast({
        title: "Success",
        description: "Collection deleted successfully",
        variant: "default",
      });
      queryClient.invalidateQueries({ queryKey: ['collections'] });
      // Call the onCollectionsChange callback if provided
      if (onCollectionsChange) onCollectionsChange();
    },
    onError: (error: any) => {
      toast({
        title: "Error",
        description: `Failed to delete collection: ${error.message}`,
        variant: "destructive",
      });
    }
  });

  // Ingest file mutation
  const ingestFileMutation = useMutation({
    mutationFn: async (data: IngestFileRequest & { file: File }) => apiClient.ingestFile(data),
    onSuccess: () => {
      toast({
        title: "Success",
        description: "File uploaded successfully",
        variant: "default",
      });
      queryClient.invalidateQueries({ queryKey: ['collections'] });
      // Call the onCollectionsChange callback if provided
      if (onCollectionsChange) onCollectionsChange();
      setFileUploadModalOpen(false);
    },
    onError: (error: any) => {
      toast({
        title: "Error",
        description: `Failed to upload file: ${error.message}`,
        variant: "destructive",
      });
    }
  });

  // Remove file mutation
  const removeFileMutation = useMutation({
    mutationFn: async (data: { pathInContainer: string, collectionName: string, isBlobFile: boolean }) => 
      apiClient.removeFile({ ...data, isBlobFile: data.isBlobFile ?? true }),
    onSuccess: () => {
      toast({
        title: "Success",
        description: "File removed successfully",
        variant: "default",
      });
      queryClient.invalidateQueries({ queryKey: ['collections'] });
      // Call the onCollectionsChange callback if provided
      if (onCollectionsChange) onCollectionsChange();
    },
    onError: (error: any) => {
      toast({
        title: "Error",
        description: `Failed to remove file: ${error.message}`,
        variant: "destructive",
      });
    }
  });

  // Use the first collection as default active collection when collections load
  useEffect(() => {
    if (collections.length > 0 && !activeCollection) {
      setActiveCollection(collections[0].name);
      onSelectCollection(collections[0].name);
      
      // Expand the first collection by default
      setExpandedCollections(new Set([collections[0].name]));
    }
  }, [collections, activeCollection, onSelectCollection]);

  const toggleSidebar = () => onToggleSidebar();

  const toggleCollection = (collectionName: string) => {
    setExpandedCollections(prev => {
      const newSet = new Set(prev);
      if (newSet.has(collectionName)) {
        newSet.delete(collectionName);
      } else {
        newSet.add(collectionName);
      }
      return newSet;
    });
  };

  const handleCreateCollection = (data: any) => {
    createCollectionMutation.mutate(data);
  };

  const handleDeleteCollection = (collectionName: string) => {
    setConfirmDialogData({
      title: "Confirm Deletion",
      message: `Are you sure you want to delete collection "${collectionName}"? All files within it will be removed.`,
      action: () => {
        deleteCollectionMutation.mutate(collectionName);
        setConfirmDialogOpen(false);
      },
    });
    setConfirmDialogOpen(true);
  };

  const handleAddFile = (collectionName: string) => {
    setActiveCollection(collectionName);
    setFileUploadModalOpen(true);
  };

  const handleRemoveFile = (collectionName: string, fileName: string) => {
    setConfirmDialogData({
      title: "Confirm File Removal",
      message: `Are you sure you want to remove the file "${fileName}" from collection "${collectionName}"?`,
      action: () => {
        removeFileMutation.mutate({ 
          pathInContainer: fileName.replace(`/${collectionName}`, ''),
          collectionName: collectionName,
          isBlobFile: true // Add the required property
        });
        setConfirmDialogOpen(false);
      },
    });
    setConfirmDialogOpen(true);
  };

  // const handleFileUpload = (data: any) => {
  //   const { file, ...restData } = data;
  //   // In a real implementation, we would handle file upload to S3 or Azure Blob
  //   // and then call the ingest API with the file path
    
  //   // For now, we'll just use the file name as the path
  //   ingestFileMutation.mutate({
  //     ...restData,
  //     filePath: file.name
  //   });
  // };

  const handleFileUpload = (data: IngestFileRequest) => {
    // Pass the entire data object including the file
    ingestFileMutation.mutate(data);
  };

  if (isLoading) {
    return (
      <aside className="bg-card border-r border-secondary h-full transition-all duration-300 overflow-hidden relative" 
        style={{ width: isExpanded ? '400px' : '0px' }}>
        <div className="flex justify-center items-center h-full">
          <div className="animate-pulse text-primary">Loading...</div>
        </div>
      </aside>
    );
  }

  return (
    <>
      <aside 
        className="bg-card border-r border-secondary h-full transition-all duration-300 overflow-hidden relative flex flex-col" 
        style={{ width: isExpanded ? '400px' : '0px' }}
      >
        {isExpanded ? (
          <>
            <div className="p-4 border-b border-secondary">
              <h2 className="text-primary font-semibold">COLLECTION MANAGER</h2>
            </div>
            
            <div className="p-4 flex flex-col flex-grow overflow-hidden">
              <Button 
                className="bg-secondary hover:bg-primary text-white w-full py-2 rounded flex items-center justify-center mb-4 transition-colors"
                onClick={() => setNewCollectionModalOpen(true)}
              >
                <Plus className="mr-2 h-4 w-4" /> New Collection
              </Button>
              
              <div className="space-y-2 overflow-y-auto pr-1" style={{ maxHeight: 'calc(100vh - 160px)' }}>
                {collections.map((collection: Collection) => (
                  <div key={collection.name} className="border border-secondary rounded p-2 mb-2">
                    <div className="cursor-pointer">
                      {/* Top row with collection name and buttons */}
                      <div className="flex justify-between items-center mb-1" onClick={() => toggleCollection(collection.name)}>
                        <div className="flex items-center">
                          <FolderClosed className="text-primary mr-2 h-4 w-4" />
                          <span 
                            className={`text-accent ${activeCollection === collection.name ? 'font-semibold' : ''}`}
                            onClick={(e) => {
                              e.stopPropagation();
                              setActiveCollection(collection.name);
                              onSelectCollection(collection.name);
                            }}
                          >
                            {collection.name}
                          </span>
                        </div>
                        <div className="flex">
                          <Button 
                            variant="ghost" 
                            size="icon" 
                            className="text-primary hover:text-accent hover:bg-transparent h-6 w-6 mr-1"
                            onClick={(e) => {
                              e.stopPropagation();
                              handleAddFile(collection.name);
                            }}
                          >
                            <Plus className="h-3 w-3" />
                          </Button>
                          <Button 
                            variant="ghost" 
                            size="icon" 
                            className="text-destructive hover:text-destructive/80 hover:bg-transparent h-6 w-6"
                            onClick={(e) => {
                              e.stopPropagation();
                              handleDeleteCollection(collection.name);
                            }}
                          >
                            <Trash2 className="h-3 w-3" />
                          </Button>
                          <Button 
                            variant="ghost" 
                            size="icon" 
                            className="text-primary hover:text-accent hover:bg-transparent h-6 w-6 ml-1"
                            onClick={(e: React.MouseEvent) => e.stopPropagation()}
                          >
                            {expandedCollections.has(collection.name) ? 
                              <ChevronDown className="h-3 w-3" /> : 
                              <ChevronRight className="h-3 w-3" />
                            }
                          </Button>
                        </div>
                      </div>
                      
                      {/* Metadata row below collection name */}
                      <div className="ml-6 mb-2" onClick={() => toggleCollection(collection.name)}>
                        {collection.distanceMetric && (
                          <div className="text-xs text-muted-foreground italic">
                            description: {collection.description || 'N/A'}
                          </div>
                        )}
                        {collection.collectionType && (
                          <div className="text-xs text-muted-foreground italic">
                            collection type: {collection.collectionType}
                          </div>
                        )}
                        {collection.embeddingModel && (
                          <div className="text-xs text-muted-foreground italic">
                            embedding model: {collection.embeddingModel}
                          </div>
                        )}
                        {collection.distanceMetric && (
                          <div className="text-xs text-muted-foreground italic">
                            distance metric: {collection.distanceMetric}
                          </div>
                        )}
                      </div>
                    </div>
                  
                    {expandedCollections.has(collection.name) && (
                      <div className="pl-5 space-y-1 mt-1">
                        {collection.files && collection.files.length > 0 ? (
                          <FileList
                            files={collection.files || []}
                            collectionName={collection.name}
                            onRemoveFile={handleRemoveFile}
                          />
                        ) : (
                          <div className="text-muted-foreground text-sm italic">No files in this collection</div>
                        )}
                      </div>
                    )}
                  </div>
                ))}
                
                {collections.length === 0 && (
                  <div className="text-muted-foreground text-center p-4">
                    No collections found. Create a new collection to get started.
                  </div>
                )}
              </div>
            </div>
          </>
        ) : null}
        
        {!isExpanded && (
          <Button 
            variant="outline" 
            size="icon" 
            onClick={toggleSidebar}
            className="absolute top-1/2 -right-6 bg-card border border-secondary text-primary p-1 rounded-r-md"
          >
            <ChevronRight className="h-4 w-4" />
          </Button>
        )}
      </aside>
      
      <NewCollectionModal 
        isOpen={newCollectionModalOpen}
        embeddingModels={embeddingModels}
        onSubmit={handleCreateCollection}
        onClose={() => setNewCollectionModalOpen(false)}
      />
      
      <FileUploadModal 
        isOpen={fileUploadModalOpen}
        collectionName={activeCollection || ''}
        onSubmit={handleFileUpload}
        onClose={() => setFileUploadModalOpen(false)}
      />
      
      <ConfirmDialog 
        isOpen={confirmDialogOpen}
        title={confirmDialogData.title}
        message={confirmDialogData.message}
        onConfirm={confirmDialogData.action}
        onCancel={() => setConfirmDialogOpen(false)}
      />
    </>
  );
};

export default CollectionPanel;
