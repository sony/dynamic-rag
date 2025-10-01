// file-item.tsx
import { FC, useState } from 'react';
import { X, Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { useToast } from '@/hooks/use-toast';
import { getFileIconByExtension } from '@/lib/utils';
import { useApi } from '@/hooks/use-api';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

// Define interfaces
export interface FileInfo {
  filename: string;
  pathInContainer: string;
  sizeMB: number;
  lastModified: string;
  contentType: string;
}

interface FileItemProps {
  file: FileInfo;
  collectionName: string;
  onRemove: (collectionName: string, filePath: string) => void;
}

const FileItem: FC<FileItemProps> = ({ file, collectionName, onRemove }) => {
  const [isDownloading, setIsDownloading] = useState(false);

  // // Download file mutation
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
  //   },
  //   onSettled: () => {
  //     setIsDownloading(false);
  //   }
  // });

  const { toast } = useToast();
  const { apiClient } = useApi();
  const queryClient = useQueryClient();

  // Download file mutation
  const downloadFileMutation = useMutation({
    mutationFn: async (pathInContainer: string) => {
      const requestData = {
        pathInContainer: pathInContainer,
      };
      return apiClient.downloadSingleBlob(requestData);
    },
    onSuccess: (data) => {
      // Open the download URL in a new tab if it exists
      if (data.upload_details && data.upload_details.downloadUrl) {
        window.open(data.upload_details.downloadUrl, '_blank');
      } else {
        throw new Error('Download URL not found in response');
      }
    },
    onError: (error: any) => {
      toast({
        title: "Error",
        description: `Failed to download file: ${error.message}`,
        variant: "destructive",
      });
    },
    onSettled: () => {
      setIsDownloading(false);
    }
  });

  const handleCopyFilename = async () => {
    try {
      if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(file.filename);
      } else {
        // Fallback for environments where the Clipboard API is not available
        const textarea = document.createElement('textarea');
        textarea.value = file.filename;
        textarea.style.position = 'fixed';
        textarea.style.top = '0';
        textarea.style.left = '0';
        textarea.style.opacity = '0';
        document.body.appendChild(textarea);
        textarea.focus();
        textarea.select();
        document.execCommand('copy');
        document.body.removeChild(textarea);
      }

      toast({
        title: "Success",
        description: `"${file.filename}" copied to clipboard`,
        variant: "default",
      });
    } catch (err: any) {
      console.error(err);
      toast({
        title: "Error",
        description: "Failed to copy filename to clipboard",
        variant: "destructive",
      });
    }
  };

  const handleDownload = () => {
    setIsDownloading(true);
    
    toast({
      title: "Downloading...",
      description: `Preparing ${file.filename} for download`,
      variant: "default",
    });
    
    downloadFileMutation.mutate(file.pathInContainer);
  };

  return (
    <div className="flex items-center justify-between">
      {/* Left section: icon, filename and size */}
      <div className="flex items-center min-w-0 flex-1">
        {/* File icon (click to copy filename) */}
        <button
          type="button"
          onClick={handleCopyFilename}
          title="Copy filename"
          className="text-accent mr-2 h-4 w-4 flex-shrink-0 cursor-pointer hover:text-accent-foreground"
        >
          {getFileIconByExtension(file.filename)}
        </button>

        {/* Downloadable filename */}
        <button
          onClick={handleDownload}
          disabled={isDownloading}
          title={file.filename}
          className="text-white text-sm hover:underline cursor-pointer flex items-center overflow-x-auto whitespace-nowrap min-w-0"
        >
          {file.filename}
          {isDownloading && (
            <Loader2 className="ml-2 h-3 w-3 animate-spin flex-shrink-0" />
          )}
        </button>

        {/* File size (optional) */}
        {file.sizeMB !== undefined && (
          <span className="text-gray-400 text-xs ml-2 flex-shrink-0">
            {file.sizeMB.toFixed(2)} MB
          </span>
        )}
      </div>

      {/* Delete button pinned to right */}
      <Button 
        variant="ghost" 
        size="icon" 
        className="text-destructive hover:text-destructive/80 hover:bg-transparent h-6 w-6 flex-shrink-0 ml-2"
        onClick={() => onRemove(collectionName, file.pathInContainer)}
      >
        <X className="h-3 w-3" />
      </Button>
    </div>
  );
};

export default FileItem;