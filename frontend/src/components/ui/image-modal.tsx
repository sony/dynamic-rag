import { FC, useState } from 'react';
import { X, Download, Copy } from 'lucide-react';
import { Button } from './button';

interface ImageModalProps {
  isOpen: boolean;
  onClose: () => void;
  imageData: string;
  fileName: string;
}

const ImageModal: FC<ImageModalProps> = ({ isOpen, onClose, imageData, fileName }) => {
  const [copySuccess, setCopySuccess] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [imageError, setImageError] = useState<boolean>(false);
  
  // Determine file extension based on filename or default to png
  const fileExtension = fileName.includes('.') 
    ? fileName.split('.').pop()?.toLowerCase() || 'png'
    : 'png';
  
  // Determine MIME type based on extension
  const getMimeType = (ext: string): string => {
    const mimeTypes: Record<string, string> = {
      'png': 'image/png',
      'jpg': 'image/jpeg',
      'jpeg': 'image/jpeg',
      'gif': 'image/gif',
      'bmp': 'image/bmp',
      'webp': 'image/webp'
    };
    return mimeTypes[ext] || 'image/png';
  };
  
  const mimeType = getMimeType(fileExtension);
  const dataUrl = `data:${mimeType};base64,${imageData}`;
  
  if (!isOpen) return null;
  
  const handleDownload = () => {
    const link = document.createElement('a');
    link.href = dataUrl;
    link.download = fileName || `image.${fileExtension}`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  };
  
  // Check if clipboard API is available in this environment
  const isClipboardAvailable = typeof navigator !== 'undefined' && 
                              navigator.clipboard && 
                              typeof navigator.clipboard.write === 'function';
  
  const handleCopy = async () => {
    try {
      if (isClipboardAvailable) {
        // Create a blob from the base64 data
        const response = await fetch(dataUrl);
        const blob = await response.blob();
        
        // Use the clipboard API to copy the image if available
        await navigator.clipboard.write([
          new ClipboardItem({
            [blob.type]: blob
          })
        ]);
        setCopySuccess('Image copied to clipboard!');
      } else {
        // Open the image in a new tab for the user to right-click and save
        const newTab = window.open();
        if (newTab) {
          newTab.document.write(`
            <html>
              <head>
                <title>Copy Image: ${fileName}</title>
                <style>
                  body { 
                    margin: 0; 
                    display: flex; 
                    flex-direction: column; 
                    align-items: center; 
                    justify-content: center; 
                    min-height: 100vh; 
                    background-color: #1a1a1a; 
                    color: white; 
                    font-family: system-ui, sans-serif;
                  }
                  img { 
                    max-width: 90%; 
                    max-height: 80vh; 
                    margin: 20px 0; 
                    border: 1px solid #333; 
                  }
                  .instructions {
                    margin-top: 20px;
                    text-align: center;
                    max-width: 500px;
                    line-height: 1.5;
                  }
                </style>
              </head>
              <body>
                <h2>Right-click on the image and select "Copy Image"</h2>
                <img src="${dataUrl}" alt="${fileName}" />
                <div class="instructions">
                  <p>Your browser doesn't support automatic image copying.</p>
                  <p>Please right-click on the image above and select "Copy Image" from the context menu.</p>
                  <p>You can close this tab after copying.</p>
                </div>
              </body>
            </html>
          `);
          newTab.document.close();
          setCopySuccess('Image opened in new tab for copying');
        } else {
          // If popup is blocked, provide a download link instead
          handleDownload();
          setCopySuccess('Image downloaded (popup blocked)');
        }
      }
      
      setTimeout(() => setCopySuccess(null), 3000);
    } catch (err) {
      console.error('Failed to copy image: ', err);
      setCopySuccess('Failed to copy image');
      setTimeout(() => setCopySuccess(null), 2000);
      
      // Fallback to download if copy fails
      try {
        handleDownload();
      } catch (downloadErr) {
        console.error('Download fallback also failed:', downloadErr);
      }
    }
  };
  
  const handleImageLoad = () => {
    setIsLoading(false);
    setImageError(false);
  };
  
  const handleImageError = () => {
    setIsLoading(false);
    setImageError(true);
  };
  
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80" onClick={onClose}>
      <div 
        className="bg-card p-4 rounded-lg max-w-4xl max-h-[90vh] flex flex-col relative"
        onClick={(e) => e.stopPropagation()} // Prevent closing when clicking inside
      >
        <div className="flex justify-between items-center mb-2">
          <h3 className="text-lg font-medium text-white">{fileName}</h3>
          <Button variant="ghost" size="icon" onClick={onClose} className="h-8 w-8">
            <X className="h-4 w-4" />
          </Button>
        </div>
        
        <div className="overflow-auto flex-1 mb-4 flex items-center justify-center bg-black/20 rounded">
          {isLoading && (
            <div className="text-accent text-center p-8">
              <div className="inline-block h-8 w-8 animate-spin rounded-full border-4 border-solid border-current border-r-transparent align-[-0.125em] motion-reduce:animate-[spin_1.5s_linear_infinite] mb-2"></div>
              <p>Loading image...</p>
            </div>
          )}
          
          {imageError && (
            <div className="text-accent text-center p-8">
              <p>Failed to load image</p>
              <p className="text-sm mt-2">The image data may be corrupted or in an unsupported format.</p>
            </div>
          )}
          
          <img 
            src={dataUrl} 
            alt={fileName}
            className={`max-w-full max-h-[70vh] object-contain ${isLoading ? 'hidden' : ''}`}
            onLoad={handleImageLoad}
            onError={handleImageError}
          />
        </div>
        
        <div className="flex justify-end space-x-2">
          {copySuccess && (
            <span className="text-primary text-sm mr-2 self-center">{copySuccess}</span>
          )}
          <Button 
            onClick={handleCopy} 
            className="flex items-center" 
            disabled={isLoading || imageError}
          >
            <Copy className="mr-2 h-4 w-4" /> {isClipboardAvailable ? 'Copy Image' : 'Open for Copy'}
          </Button>
          <Button 
            onClick={handleDownload} 
            className="flex items-center"
            disabled={isLoading || imageError}
          >
            <Download className="mr-2 h-4 w-4" /> Download
          </Button>
        </div>
      </div>
    </div>
  );
};

export default ImageModal;
