import { FC, useState } from 'react';
import { Message, EmbeddingSearchResult } from '../../lib/types';
import { cn } from '../../lib/utils';
import { ChevronRight, ChevronDown, ExternalLink } from 'lucide-react';
import ReactMarkdown from 'react-markdown';
import rehypeHighlight from 'rehype-highlight';
import remarkGfm from 'remark-gfm';
import rehypeRaw from 'rehype-raw';
import ImageModal from '../ui/image-modal';

interface MessageBubbleProps {
  message: Message;
}

interface CitationItemProps {
  citation: EmbeddingSearchResult;
  rank: number;
}

const CitationItem: FC<CitationItemProps> = ({ citation, rank }) => {
  const [expanded, setExpanded] = useState(false);
  const hasImage = !!citation.imageData;
  
  return (
    <div className="mb-1">
      <div 
        className="flex items-center cursor-pointer text-accent hover:text-primary transition-colors" 
        onClick={() => setExpanded(!expanded)}
      >
        {expanded ? 
          <ChevronDown className="h-3 w-3 mr-1" /> : 
          <ChevronRight className="h-3 w-3 mr-1" />}
        <span>[{rank + 1}] {citation.fileName} (chunk {citation.chunkIndex})</span>
        {hasImage && <span className="ml-2 text-primary">[Image]</span>}
      </div>
      
      {expanded && (
        <div className="ml-4 mt-1 p-2 bg-black/30 rounded border border-accent/20 text-xs">
          <div className="grid grid-cols-[100px_1fr] gap-1">
            <span className="text-accent">Rank:</span>
            <span>{citation.rank}</span>

            <span className="text-accent">Score:</span>
            <span>{citation.score}</span>

            <span className="text-accent">File:</span>
            <span>{citation.fileName}</span>
            
            <span className="text-accent">Chunk:</span>
            <span>{citation.chunkIndex}</span>

            <span className="text-accent">Model:</span>
            <span>{citation.embeddingModelName}</span>
            
            {citation.timestamp && (
              <>
                <span className="text-accent">Timestamp:</span>
                <span>{new Date(citation.timestamp).toLocaleString()}</span>
              </>
            )}
          </div>
          <div className="mt-2">
            <span className="text-accent">Definition:</span>
            <div className="mt-1 prose prose-invert prose-sm max-w-none">
              <ReactMarkdown
                remarkPlugins={[remarkGfm]}
                rehypePlugins={[rehypeHighlight, rehypeRaw]}
                components={{
                  // Override pre to prevent default styling
                  pre: ({ children }: any) => <>{children}</>,
                  code: ({ node, className, children, ...props }: any) => {
                    const match = /language-(\w+)/.exec(className || '');
                    const isInline = !className;
                    return !isInline && match ? (
                      <div className="not-prose my-4">
                        <pre className="p-0 m-0 bg-transparent">
                          <code className={`block language-${match[1]} rounded p-4 overflow-auto border border-accent/20 bg-black/30 text-base`} {...props}>
                            {children}
                          </code>
                        </pre>
                      </div>
                    ) : (
                      <code className="bg-black/20 px-1 py-0.5 rounded text-base" {...props}>
                        {children}
                      </code>
                    );
                  }
                }}
              >
                {citation.definition}
              </ReactMarkdown>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

const MessageBubble: FC<MessageBubbleProps> = ({ message }) => {
  const isUser = message.role === 'user';
  
  // State for image modal
  const [selectedImage, setSelectedImage] = useState<{
    data: string;
    fileName: string;
  } | null>(null);
  
  // Check if any search results have images
  const hasImages = !isUser && message.searchResults?.some((result: EmbeddingSearchResult) => !!result.imageData);
  
  const handleImageClick = (imageData: string, fileName: string) => {
    setSelectedImage({ data: imageData, fileName });
  };
  
  const closeModal = () => {
    setSelectedImage(null);
  };
  
  return (
    <>
      {/* Image modal */}
      {selectedImage && (
        <ImageModal
          isOpen={!!selectedImage}
          onClose={closeModal}
          imageData={selectedImage.data}
          fileName={selectedImage.fileName}
        />
      )}
      
      <div className="flex mb-2">
        <div 
          className={cn(
            "p-3 rounded-lg max-w-3xl",
            isUser 
              ? "bg-card text-white ml-auto" 
              : "bg-secondary text-white"
          )}
        >
        {isUser ? (
          <p>{message.content}</p>
        ) : (
          <div className="prose prose-invert prose-base max-w-none">
            <ReactMarkdown
              remarkPlugins={[remarkGfm]}
              rehypePlugins={[rehypeHighlight, rehypeRaw]}
              components={{
                // Override pre to prevent default styling
                pre: ({ children }: any) => <>{children}</>,
                code: ({ node, className, children, ...props }: any) => {
                  const match = /language-(\w+)/.exec(className || '');
                  const isInline = !className;
                  return !isInline && match ? (
                    <div className="not-prose my-4">
                      <pre className="p-0 m-0 bg-transparent">
                        <code className={`block language-${match[1]} rounded p-4 overflow-auto border border-accent/20 bg-black/30 text-base`} {...props}>
                          {children}
                        </code>
                      </pre>
                    </div>
                  ) : (
                    <code className="bg-black/20 px-1 py-0.5 rounded text-base" {...props}>
                      {children}
                    </code>
                  );
                },
                // Style links to be more visible
                a: ({ node, ...props }: any) => <a className="text-primary hover:underline" {...props} />,
              }}
            >
              {message.content}
            </ReactMarkdown>
          </div>
        )}
        
        {/* Display sections only for system messages (not user) */}
        {!isUser && message.searchResults && message.searchResults.length > 0 && (
          <>
            {/* Display images in a column if available */}
            {hasImages && (
              <div className="mt-4 pt-3 border-t border-accent/20">
                <p className="text-accent font-semibold mb-3">Image Results:</p>
                <div className="flex flex-wrap gap-4">
                  {message.searchResults
                    ?.filter((result: EmbeddingSearchResult) => !!result.imageData)
                    .map((result: EmbeddingSearchResult, index: number) => (
                      <div key={`img-${result.fileName}-${result.chunkIndex}`} className="flex flex-col bg-black/20 p-3 rounded-md">
                        {/* <div className="flex justify-between items-center mb-2">
                          <span className="text-sm text-accent">[{index + 1}] {result.fileName}</span>
                          {result.score && <div className="text-xs text-accent/80">Score: {parseFloat(result.score).toFixed(4)}</div>}
                        </div> */}
                        <div className="flex justify-between items-start mb-2 flex-col">
                          <span className="text-sm text-accent">[{index + 1}] {result.fileName}</span>
                          {result.score && (
                            <div className="text-xs text-accent/80">
                              Score: {parseFloat(result.score).toFixed(4)}
                            </div>
                          )}
                        </div>
                        <div 
                          className="cursor-pointer relative group"
                          onClick={() => handleImageClick(result.imageData || '', result.fileName)}
                        >
                          <div className="absolute inset-0 flex items-center justify-center bg-black/50 opacity-0 group-hover:opacity-100 transition-opacity rounded">
                            <ExternalLink className="h-6 w-6 text-white" />
                          </div>
                          <img 
                            src={`data:image/png;base64,${result.imageData}`} 
                            alt={`${result.fileName}`}
                            className="w-[150px] h-[150px] object-contain bg-black/30 rounded border border-accent/20 hover:border-primary transition-colors"
                          />
                        </div>
                      </div>
                  ))}
                </div>
              </div>
            )}
            
            {/* Citations section */}
            <div className="mt-3 pt-2 border-t border-accent/20 text-xs">
              <p className="text-accent mb-1">Citations:</p>
              <div className="ml-1">
                {message.searchResults.map((result: EmbeddingSearchResult, index: number) => (
                  <CitationItem 
                    key={`${result.fileName}-${result.chunkIndex}`}
                    citation={result} 
                    rank={index}
                  />
                ))}
              </div>
            </div>
          </>
        )}
        </div>
      </div>
    </>
  );
};

export default MessageBubble;
