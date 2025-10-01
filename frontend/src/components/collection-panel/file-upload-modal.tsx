import { FC, useState, useRef } from 'react';
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { FileUploadModalProps } from '@/lib/types';
import { X, CloudUpload, File as FileIcon } from 'lucide-react';
import { SUPPORTED_FILE_TYPES } from '@/lib/constants';
import { formatFileSize } from '@/lib/utils';

const FileUploadModal: FC<FileUploadModalProps> = ({
  isOpen,
  collectionName,
  onSubmit,
  onClose
}) => {
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [chunkSize, setChunkSize] = useState(1000);
  const [chunkOverlap, setChunkOverlap] = useState(0.1);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    
    if (!selectedFile) return;
    
    onSubmit({
      file: selectedFile,
      filePath: selectedFile.name, // TODO: return to this to support multi-level uploads
      collectionName,
      chunkSize,
      chunkOverlapFraction: chunkOverlap,
      saveToBlobStorage: true
    });
    
    resetForm();
  };

  const resetForm = () => {
    setSelectedFile(null);
    setChunkSize(1000);
    setChunkOverlap(0.1);
  };

  const handleClose = () => {
    resetForm();
    onClose();
  };

  const handleFileSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files.length > 0) {
      setSelectedFile(e.target.files[0]);
    }
  };

  const handleClearFile = () => {
    setSelectedFile(null);
    if (fileInputRef.current) {
      fileInputRef.current.value = '';
    }
  };

  const triggerFileSelect = () => {
    if (fileInputRef.current) {
      fileInputRef.current.click();
    }
  };

  return (
    <Dialog open={isOpen} onOpenChange={handleClose}>
      <DialogContent className="bg-card border border-primary max-w-md">
        <DialogHeader className="mb-4">
          <DialogTitle className="text-xl text-primary font-bold">Upload File</DialogTitle>
        </DialogHeader>
        
        <form onSubmit={handleSubmit}>
          <div className="mb-4">
            <p className="text-accent mb-2">
              Uploading to collection: <span className="font-bold">{collectionName}</span>
            </p>
          </div>
          
          <div className="mb-4">
            <Label htmlFor="file-upload" className="block text-accent mb-2">
              Select File
            </Label>
            <div 
              className={`relative border-2 border-dashed ${selectedFile ? 'border-primary' : 'border-secondary'} rounded-md p-4 text-center cursor-pointer hover:border-accent transition-colors`}
              onClick={triggerFileSelect}
            >
              <input 
                id="file-upload" 
                ref={fileInputRef}
                type="file" 
                className="hidden" 
                onChange={handleFileSelect}
                accept={SUPPORTED_FILE_TYPES.join(',')}
              />
              
              {!selectedFile ? (
                <div className="py-8">
                  <CloudUpload className="h-12 w-12 text-primary mx-auto mb-2" />
                  <p className="text-accent">Click to select or drag a file here</p>
                  <p className="text-xs text-accent mt-1">
                    Supported: PDF, DOCX, TXT, CSV, JSON, XML, XLSX, XLS, PNG, JPEG, BMP
                  </p>
                </div>
              ) : (
                <div className="py-4">
                  <FileIcon className="h-10 w-10 text-primary mx-auto mb-2" />
                  <p className="text-accent font-bold">{selectedFile.name}</p>
                  <p className="text-xs text-accent mt-1">{formatFileSize(selectedFile.size)}</p>
                  <Button 
                    type="button" 
                    variant="ghost" 
                    size="sm" 
                    onClick={(e) => {
                      e.stopPropagation();
                      handleClearFile();
                    }}
                    className="text-destructive text-sm mt-2 hover:bg-transparent"
                  >
                    <X className="h-3 w-3 mr-1" /> Clear
                  </Button>
                </div>
              )}
            </div>
          </div>
          
          <div className="mb-4">
            <Label htmlFor="chunk-size" className="block text-accent mb-2">
              Chunk Size
            </Label>
            <Input 
              id="chunk-size"
              type="number"
              min={100}
              max={40000}
              value={chunkSize}
              onChange={(e) => setChunkSize(parseInt(e.target.value))}
              className="w-full bg-black border border-secondary rounded py-2 px-3 text-white focus:outline-none focus:border-primary"
              required
            />
            <p className="text-xs text-primary mt-1">Recommended: 1000-2000 for most documents</p>
          </div>
          
          <div className="mb-4">
            <Label htmlFor="chunk-overlap" className="block text-accent mb-2">
              Chunk Overlap Fraction
            </Label>
            <Input 
              id="chunk-overlap"
              type="number"
              min={0}
              max={0.5}
              step={0.05}
              value={chunkOverlap}
              onChange={(e) => setChunkOverlap(parseFloat(e.target.value))}
              className="w-full bg-black border border-secondary rounded py-2 px-3 text-white focus:outline-none focus:border-primary"
              required
            />
            <p className="text-xs text-primary mt-1">Recommended: 0.1-0.2 for most documents</p>
          </div>
          
          <div className="flex justify-end space-x-3">
            <Button 
              type="button" 
              variant="outline"
              onClick={handleClose}
              className="bg-card border border-secondary text-primary px-4 py-2 rounded hover:bg-black transition-colors"
            >
              CANCEL
            </Button>
            <Button 
              type="submit"
              disabled={!selectedFile}
              className="bg-secondary text-white px-4 py-2 rounded hover:bg-primary transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
            >
              UPLOAD
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  );
};

export default FileUploadModal;
