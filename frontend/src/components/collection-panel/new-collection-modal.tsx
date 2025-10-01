import { FC, useState, useEffect } from 'react';
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Checkbox } from '@/components/ui/checkbox';
import { Info, X } from 'lucide-react';
import { NewCollectionModalProps } from '@/lib/types';
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip';
import { set } from 'date-fns';

const NewCollectionModal: FC<NewCollectionModalProps> = ({
  isOpen,
  embeddingModels,
  onSubmit,
  onClose
}: {
  isOpen: boolean;
  embeddingModels: { ImageEmbedding: string[], TextEmbedding: string[] };
  onSubmit: (data: { collectionName: string; defaultEmbeddingModelName: string; description: string; initHNSW: boolean; initBTree: boolean; embeddingType: string }) => void;
  onClose: () => void;
}) => {
  const [collectionName, setCollectionName] = useState('');
  const [embeddingType, setEmbeddingType] = useState('TextEmbedding');
  const [embeddingModel, setEmbeddingModel] = useState('');
  const [description, setDescription] = useState('');
  const [initHNSW, setInitHNSW] = useState(true);
  const [initBTree, setInitBTree] = useState(true);
  const [isValidName, setIsValidName] = useState(false);
  const [nameError, setNameError] = useState('');

  // PostgreSQL naming validation
  const validateCollectionName = (name: string) => {
    // Check if empty
    if (!name.trim()) {
      setNameError('Collection name is required');
      return false;
    }
    
    // Check first character (must be letter or underscore)
    if (!/^[a-zA-Z_]/.test(name)) {
      setNameError('Must start with a letter or underscore');
      return false;
    }
    
    // Check for valid characters (letters, numbers, underscores only)
    if (!/^[a-zA-Z0-9_]+$/.test(name)) {
      setNameError('Only letters, numbers, and underscores allowed');
      return false;
    }
    
    // Check for reserved keywords (simplified list)
    const reservedKeywords = ['select', 'from', 'where', 'table', 'index', 'primary', 'foreign', 'key', 'constraint'];
    if (reservedKeywords.includes(name.toLowerCase())) {
      setNameError('Name is a reserved SQL keyword');
      return false;
    }
    
    // Valid name
    setNameError('');
    return true;
  };

  // Validate name whenever it changes
  useEffect(() => {
    const isValid = validateCollectionName(collectionName);
    setIsValidName(isValid);
  }, [collectionName]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (isValidName && embeddingModel) {
      onSubmit({
        collectionName,
        defaultEmbeddingModelName: embeddingModel,
        description,
        initHNSW,
        initBTree,
        embeddingType
      });
      resetForm();
    }
  };

  const resetForm = () => {
    setCollectionName('');
    setEmbeddingType('TextEmbedding');
    setEmbeddingModel('');
    setDescription('');
    setInitHNSW(true);
    setInitBTree(true);
    setNameError('');
    setIsValidName(false);
  };

  const handleClose = () => {
    resetForm();
    onClose();
  };

  return (
    <Dialog open={isOpen} onOpenChange={handleClose}>
      <DialogContent className="bg-card border border-primary max-w-md">
        <DialogHeader className="mb-4">
          <DialogTitle className="text-xl text-primary font-bold">New Collection</DialogTitle>
        </DialogHeader>
        
        <form onSubmit={handleSubmit}>
          <div className="mb-4">
            <Label htmlFor="collection-name" className="block text-accent mb-2">
              Collection Name
            </Label>
            <div className="relative">
              <Input 
                id="collection-name"
                value={collectionName}
                onChange={(e) => setCollectionName(e.target.value)}
                className="w-full bg-black border border-secondary rounded py-2 px-3 text-white focus:outline-none focus:border-primary pr-16"
                maxLength={50}
                required
              />
              <div className="absolute right-3 top-2 text-xs text-primary">
                {collectionName.length}/50
              </div>
              {nameError ? (
                <p className="text-red-500 text-xs mt-1">{nameError}</p>
              ) : (
                <p className="text-xs text-accent mt-1">
                  Must start with a letter or underscore. Only letters, numbers, and underscores allowed.
                </p>
              )}
            </div>
          </div>

          <div className="mb-4">
            <Label htmlFor="collection-description" className="block text-accent mb-2">
              Description
            </Label>
            <Input
              id="collection-description"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              className="w-full bg-black border border-secondary rounded py-2 px-3 text-white focus:outline-none focus:border-primary"
              maxLength={200}
              placeholder="Optional description for this collection"
            />
            <div className="text-xs text-accent mt-1">{description.length}/200</div>
          </div>

          <div className="mb-4">
            <div className="flex items-center mb-2">
              <Label htmlFor="embedding-type" className="block text-accent">
                Embedding Type
              </Label>
              <TooltipProvider>
                <Tooltip>
                  <TooltipTrigger asChild>
                    <Button variant="ghost" size="icon" className="ml-1 h-5 w-5 p-0 text-accent hover:bg-transparent">
                      <Info className="h-4 w-4" />
                    </Button>
                  </TooltipTrigger>
                  <TooltipContent className="max-w-md bg-black border border-secondary p-4 text-white">
                    <div className="space-y-2">
                      <h4 className="font-bold underline">Text Embedding Collections</h4>
                      <p>Supports text-to-text search for language context retrieval.</p>
                      <p><span className="font-semibold">Accepted file types:</span> <span className="italic">.xlsx, .csv, .xml, .json, .jsonl, .txt, .pdf, .docx, .png, .jpg, .jpeg, .bmp</span></p>
                      
                      <h4 className="font-bold underline mt-3">Image Embedding Collections</h4>
                      <p>Supports text-to-image and image-to-image search.</p>
                      <p><span className="font-semibold">Accepted file types:</span> <span className="italic">.png, .jpg, .jpeg, .bmp</span></p>
                    </div>
                  </TooltipContent>
                </Tooltip>
              </TooltipProvider>
            </div>
            <Select 
              value={embeddingType} 
              onValueChange={(value: string) => {
                setEmbeddingType(value);
                setEmbeddingModel(''); // Reset the model when type changes
              }}
              required
            >
              <SelectTrigger className="w-full bg-black border border-secondary rounded py-2 px-3 text-white focus:outline-none focus:border-primary">
                <SelectValue placeholder="Select an embedding type" />
              </SelectTrigger>
              <SelectContent className="bg-black border border-secondary">
                <SelectItem value="TextEmbedding">Text Embedding</SelectItem>
                <SelectItem value="ImageEmbedding">Image Embedding</SelectItem>
              </SelectContent>
            </Select>
          </div>
          
          <div className="mb-4">
            <Label htmlFor="embedding-model" className="block text-accent mb-2">
              Default Embedding Model
            </Label>
            <Select 
              value={embeddingModel} 
              onValueChange={setEmbeddingModel}
              required
            >
              <SelectTrigger className="w-full bg-black border border-secondary rounded py-2 px-3 text-white focus:outline-none focus:border-primary">
                <SelectValue placeholder="Select an embedding model" />
              </SelectTrigger>
              <SelectContent className="bg-black border border-secondary">
                {(embeddingModels[embeddingType as keyof typeof embeddingModels] || []).map((model: string) => (
                  <SelectItem key={model} value={model}>
                    {model}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          
          <div className="flex justify-between mb-4">
            <div className="flex items-center space-x-2">
              <Checkbox 
                id="init-hnsw" 
                checked={initHNSW}
                onCheckedChange={(checked: boolean) => setInitHNSW(checked)}
                className="border-secondary"
              />
              <Label htmlFor="init-hnsw" className="text-accent">
                Initialize HNSW
              </Label>
            </div>
            
            <div className="flex items-center space-x-2">
              <Checkbox 
                id="init-btree" 
                checked={initBTree}
                onCheckedChange={(checked: boolean) => setInitBTree(checked)}
                className="border-secondary"
              />
              <Label htmlFor="init-btree" className="text-accent">
                Initialize BTree
              </Label>
            </div>
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
              className="bg-secondary text-white px-4 py-2 rounded hover:bg-primary transition-colors"
              disabled={!isValidName || !embeddingModel}
            >
              CREATE
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  );
};

export default NewCollectionModal;
