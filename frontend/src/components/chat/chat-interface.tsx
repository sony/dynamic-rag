import { FC, useState, useRef, useEffect } from 'react';
import { Send, Menu, Filter, Paperclip } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
// Import UI components with relative paths to avoid module resolution issues
import { Switch } from '../ui/switch';
import { Label } from '../ui/label';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
  DialogFooter,
} from '../ui/dialog';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { useToast } from '@/hooks/use-toast';
import { Message, EmbeddingSearchResult } from '@/lib/types';
import MessageBubble from './message-bubble';
import { useApi } from '@/hooks/use-api';
import { useMutation, useQuery } from '@tanstack/react-query';
import { generateRandomId } from '@/lib/utils';

interface ChatInterfaceProps {
  activeCollection: string;
  collections: string[];
  onSelectCollection: (name: string) => void;
  onToggleSidebar: () => void;
  collectionsWithFiles?: any[];
}

const ChatInterface: FC<ChatInterfaceProps> = ({ 
  activeCollection, 
  collections, 
  onSelectCollection,
  onToggleSidebar,
  collectionsWithFiles = []
}) => {
  const [messages, setMessages] = useState<Message[]>([]);
  const [inputValue, setInputValue] = useState('');
  const [chatModel, setChatModel] = useState('');
  const [selectedImage, setSelectedImage] = useState<File | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [resultCount, setResultCount] = useState<number>(5);
  const [timeWindowEnabled, setTimeWindowEnabled] = useState<boolean>(false);
  const [windowDays, setWindowDays] = useState<number>(30);
  const [temporalDecayEnabled, setTemporalDecayEnabled] = useState<boolean>(false);
  const [decayImpact, setDecayImpact] = useState<number>(0.01);
  const [isFilterDialogOpen, setIsFilterDialogOpen] = useState<boolean>(false);
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const { toast } = useToast();
  const { apiClient } = useApi();
  
  const chatContainerRef = useRef<HTMLDivElement>(null);

  // Fetch chat models
  const { data: chatModels = [] } = useQuery({
    queryKey: ['chatModels'],
    queryFn: async () => { 
      const response = await apiClient.getChatModels();
      console.log("Chat Models Response: ", response);
      return response.chat_models;
    }
  });

  // Chat mutation
  const chatMutation = useMutation({
    mutationFn: async (params: { query?: string; image?: File }) => {
      const { query, image } = params;
      
      // If we have an image and this is an ImageEmbedding collection, use imageSearchWithImage
      if (image && isImageEmbeddingCollection) {
        return await apiClient.imageSearchWithImage(
          image,
          activeCollection,
          resultCount,
          timeWindowEnabled,
          windowDays,
          temporalDecayEnabled,
          decayImpact
        );
      } else if (query) {
        // For text queries, route differently based on collection type
        if (isImageEmbeddingCollection) {
          // For ImageEmbedding collections, use the text-search endpoint
          return await apiClient.textSearchImage(
            query,
            activeCollection,
            resultCount,
            timeWindowEnabled,
            windowDays,
            temporalDecayEnabled,
            decayImpact
          );
        } else {
          // For TextEmbedding collections, continue using the chat endpoint
          return apiClient.chatWithCollection({
            query,
            collectionName: activeCollection,
            nResults: resultCount,
            chatModelName: chatModel,
            timeWindowEnabled: timeWindowEnabled,
            windowDays: windowDays,
            temporalDecayEnabled: temporalDecayEnabled,
            decayImpact: decayImpact
          });
        }
      } else {
        throw new Error("Either query or image must be provided");
      }
    },
    onError: (error: any) => {
      toast({
        title: "Error",
        description: `Failed to get response: ${error.message}`,
        variant: "destructive",
      });
    }
  });

  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  };

  // Determine if the active collection is of type ImageEmbedding
  const isImageEmbeddingCollection = collectionsWithFiles?.some(
    collection => collection.name === activeCollection && collection.collectionType === 'ImageEmbedding'
  );

  // Set default chat model when chat models are loaded
  useEffect(() => {
    if (Array.isArray(chatModels) && chatModels.length > 0 && !chatModel) {
      setChatModel(chatModels[0]);
    }
  }, [chatModels, chatModel]);

  useEffect(() => {
    scrollToBottom();
  }, [messages]);

  const handleImageSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files.length > 0) {
      setSelectedImage(e.target.files[0]);
      setInputValue(''); // Clear text input when image is selected
    }
  };

  const clearImage = () => {
    setSelectedImage(null);
    if (fileInputRef.current) {
      fileInputRef.current.value = '';
    }
  };

  const handleSendMessage = async (e: React.FormEvent) => {
    e.preventDefault();
    
    if ((!inputValue.trim() && !selectedImage) || (isImageEmbeddingCollection && !selectedImage && !inputValue.trim())) return;
    if (!activeCollection) {
      toast({
        title: "Error",
        description: "Please select a collection first",
        variant: "destructive",
      });
      return;
    }
  
    // Add check for chat model (only needed for text queries, not for image search)
    if (!selectedImage && !chatModel) {
      toast({
        title: "Error",
        description: "Please select a chat model first",
        variant: "destructive",
      });
      return;
    }
    
    // Add user message
    const userMessage: Message = {
      id: generateRandomId(),
      role: 'user',
      content: selectedImage ? `[Image: ${selectedImage.name}]` : inputValue,
      timestamp: new Date()
    };
    
    setMessages(prev => [...prev, userMessage]);
    setInputValue('');
    
    try {
      // Call the appropriate API based on whether we have an image or text
      const response = await chatMutation.mutateAsync({
        query: selectedImage ? undefined : inputValue,
        image: selectedImage || undefined
      });
      
      // Clear the selected image after sending
      if (selectedImage) {
        clearImage();
      }

      console.log("Got response: ", response);
      
      // Add system message
      const systemMessage: Message = {
        id: generateRandomId(),
        role: 'system',
        content: response.final_answer,
        // Keep the legacy sources format for backward compatibility
        sources: response.search_results.map((r: EmbeddingSearchResult) => `${r.fileName} (chunk ${r.chunkIndex})`).join(', '),
        // Store the full search results
        searchResults: response.search_results,
        timestamp: new Date()
      };
      
      setMessages(prev => [...prev, systemMessage]);
    } catch (error) {
      // Error is handled by the mutation
    }
  };

  return (
    <main className="flex-1 flex flex-col bg-black">
      {/* Collection selection and chat info */}
      <div className="bg-card border-b border-secondary p-3 flex items-center justify-between">
        <div className="flex items-center">
          <Button
            variant="ghost"
            size="icon"
            onClick={onToggleSidebar}
            className="text-primary hover:text-accent hover:bg-transparent mr-2"
          >
            <Menu className="h-5 w-5" />
          </Button>
          
          <span className="text-primary mr-2">ACTIVE COLLECTION:</span>
          <Select 
            value={activeCollection} 
            onValueChange={onSelectCollection}
            disabled={collections.length === 0}
          >
            <SelectTrigger className="bg-black border border-secondary text-accent px-2 py-1 rounded text-sm w-80">
              <SelectValue placeholder="Select collection" />
            </SelectTrigger>
            <SelectContent className="bg-black border border-secondary">
              {collections.map(collection => (
                <SelectItem key={collection} value={collection}>
                  {collection}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="flex items-center justify-between space-x-4">
          <div className="flex items-center">
            <span className="text-primary text-sm mr-2">CHAT MODEL:</span>
            <Select 
              value={chatModel} 
              onValueChange={setChatModel}
            >
              <SelectTrigger className="bg-black border border-secondary text-accent px-2 py-1 rounded text-sm w-80">
                <SelectValue placeholder="Select chat model" />
              </SelectTrigger>
              <SelectContent className="bg-black border border-secondary">
              {(Array.isArray(chatModels) ? chatModels : []).map(model => (
                <SelectItem key={model} value={model}>
                  {model}
                </SelectItem>
              ))}
              </SelectContent>
            </Select>
          </div>
          
          <Dialog open={isFilterDialogOpen} onOpenChange={setIsFilterDialogOpen}>
            <DialogTrigger asChild>
              <Button 
                variant="outline" 
                size="icon" 
                className="bg-black border border-secondary text-accent hover:bg-secondary hover:text-white"
              >
                <Filter className="h-4 w-4" />
                <span className="sr-only">Filter settings</span>
              </Button>
            </DialogTrigger>
            <DialogContent className="bg-black border border-secondary text-white">
              <DialogHeader>
                <DialogTitle className="text-white">Search Settings</DialogTitle>
                <DialogDescription className="text-muted-foreground">
                  Configure search parameters and filters
                </DialogDescription>
              </DialogHeader>
              
              <div className="space-y-6 py-4">
                <div className="space-y-2">
                  <Label htmlFor="result-count" className="text-white">Search Result Count</Label>
                  <Input
                    id="result-count"
                    type="number"
                    min="1"
                    max="100"
                    value={resultCount.toString()}
                    onChange={(e: React.ChangeEvent<HTMLInputElement>) => {
                      const value = parseInt(e.target.value);
                      if (!isNaN(value) && value > 0) {
                        setResultCount(value);
                      }
                    }}
                    className="bg-black border border-secondary text-accent"
                  />
                  <p className="text-xs text-muted-foreground">Number of search results to return (1-100)</p>
                </div>
                
                <div className="space-y-4">
                  <div className="flex items-center justify-between space-x-4">
                    <div className="flex-1">
                      <Label htmlFor="time-window-toggle" className="text-white">Time Window Filter</Label>
                      <p className="text-xs text-muted-foreground">Only include results from the specified time period</p>
                    </div>
                    <div className="flex-shrink-0">
                      <Switch
                        id="time-window-toggle"
                        checked={timeWindowEnabled}
                        onCheckedChange={setTimeWindowEnabled}
                        aria-label="Toggle time window filter"
                      />
                    </div>
                  </div>
                  
                  <div className="space-y-2">
                    <Label htmlFor="window-days" className="text-white">Window Size (Days)</Label>
                    <Input
                      id="window-days"
                      type="number"
                      min="1"
                      max="3650"
                      value={windowDays.toString()}
                      onChange={(e: React.ChangeEvent<HTMLInputElement>) => {
                        const value = parseInt(e.target.value);
                        if (!isNaN(value) && value >= 1 && value <= 3650) {
                          setWindowDays(value);
                        }
                      }}
                      disabled={!timeWindowEnabled}
                      className="bg-black border border-secondary text-accent"
                    />
                    <p className="text-xs text-muted-foreground">Number of days to include (1-3650)</p>
                  </div>
                  
                  <div className="flex items-center justify-between space-x-4 mt-6">
                    <div className="flex-1">
                      <Label htmlFor="temporal-decay-toggle" className="text-white">Exponential Recency Decay</Label>
                      <p className="text-xs text-muted-foreground">Enables exponential decay function to help prioritize more recent data.</p>
                    </div>
                    <div className="flex-shrink-0">
                      <Switch
                        id="temporal-decay-toggle"
                        checked={temporalDecayEnabled}
                        onCheckedChange={setTemporalDecayEnabled}
                        aria-label="Toggle temporal decay"
                      />
                    </div>
                  </div>
                  
                  <div className="space-y-2">
                    <Label htmlFor="decay-impact" className="text-white">Recency Impact</Label>
                    <Input
                      id="decay-impact"
                      type="number"
                      min="0.001"
                      max="0.1"
                      step="0.001"
                      value={decayImpact.toString()}
                      onChange={(e: React.ChangeEvent<HTMLInputElement>) => {
                        const value = parseFloat(e.target.value);
                        if (!isNaN(value) && value >= 0.001 && value <= 0.1) {
                          setDecayImpact(value);
                        }
                      }}
                      disabled={!temporalDecayEnabled}
                      className="bg-black border border-secondary text-accent"
                    />
                    <p className="text-xs text-muted-foreground">Increase to prioritize more recently uploaded assets (0.005 - low, 0.01 - medium, 0.05 - high).</p>
                  </div>
                </div>
              </div>
              
              <DialogFooter>
                <Button 
                  onClick={() => setIsFilterDialogOpen(false)}
                  className="bg-primary hover:bg-secondary text-white"
                >
                  Apply Settings
                </Button>
              </DialogFooter>
            </DialogContent>
          </Dialog>
        </div>
      </div>
      
      {/* Chat messages area */}
      <div 
        ref={chatContainerRef}
        className="flex-1 p-4 overflow-y-auto bg-black"
      >
        {messages.length === 0 ? (
          <div className="flex items-center justify-center h-full">
            <div className="text-muted-foreground text-center">
              <p>No messages yet</p>
              <p className="text-sm">Start a conversation by sending a message</p>
              <p className="text-sm">(warning: conversation history will not persist between sessions.)</p>
            </div>
          </div>
        ) : (
          <div className="space-y-4">
            {messages.map((message) => (
              <MessageBubble key={message.id} message={message} />
            ))}
            <div ref={messagesEndRef} />
          </div>
        )}
      </div>
      
      {/* Chat input area */}
      <div className="p-4 border-t border-secondary bg-card">
        <form className="flex items-center" onSubmit={handleSendMessage}>
          {isImageEmbeddingCollection && (
            <div className="flex items-center mr-2">
              <input
                type="file"
                ref={fileInputRef}
                accept=".png,.jpg,.jpeg,.bmp"
                className="hidden"
                onChange={handleImageSelect}
                disabled={chatMutation.isPending}
              />
              <Button
                type="button"
                onClick={() => fileInputRef.current?.click()}
                className={`bg-secondary hover:bg-primary text-white py-2 px-4 rounded transition-colors ${selectedImage ? 'bg-primary' : ''}`}
                disabled={chatMutation.isPending || !!inputValue.trim()}
              >
                <Paperclip className="mr-2 h-4 w-4" /> Attach Image
              </Button>
              {selectedImage && (
                <div className="ml-2 flex items-center bg-black border border-secondary rounded p-2">
                  <span className="text-white text-sm truncate max-w-[100px]">{selectedImage.name}</span>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    className="ml-2 text-primary hover:text-accent"
                    onClick={clearImage}
                  >
                    ×
                  </Button>
                </div>
              )}
              {isImageEmbeddingCollection && <span className="mx-2 text-primary">OR</span>}
            </div>
          )}
          <div className={`relative ${isImageEmbeddingCollection ? 'flex-1' : 'flex-1 mr-2'}`}>
            <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
              <span className="text-primary">{'>'}</span>
            </div>
            <Input 
              value={inputValue}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => {
                setInputValue(e.target.value);
                if (e.target.value.trim() && selectedImage) {
                  clearImage();
                }
              }}
              className="w-full bg-black border border-secondary rounded py-2 pl-8 pr-4 text-white focus:outline-none focus:border-primary cursor-blink"
              placeholder="Type your question here..."
              disabled={chatMutation.isPending || (isImageEmbeddingCollection && !!selectedImage)}
            />
          </div>
          <div className="ml-2">
            <Button 
              type="submit"
              className="bg-secondary hover:bg-primary text-white py-2 px-4 rounded transition-colors"
              disabled={chatMutation.isPending || (!inputValue.trim() && !selectedImage)}
            >
              <Send className="mr-2 h-4 w-4" /> SEND
            </Button>
          </div>
        </form>
      </div>
    </main>
  );
};

export default ChatInterface;
