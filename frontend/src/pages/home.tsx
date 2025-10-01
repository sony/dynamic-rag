import { FC, useState } from 'react';
import Header from '@/components/header';
import CollectionPanel from '@/components/collection-panel/collection-panel';
import ChatInterface from '@/components/chat/chat-interface';
import { useCollections, CollectionWithFiles } from '../hooks/use-collections';

const Home: FC = () => {
  const [activeCollection, setActiveCollection] = useState<string>('');
  const [sidebarExpanded, setSidebarExpanded] = useState<boolean>(true);
  
  // Use the collections hook with refetch function
  const { collections: collectionsWithFiles, refetch: refetchCollections } = useCollections();
  
  // Debug collections
  console.log('Home component - collectionsWithFiles:', collectionsWithFiles);

  const handleSelectCollection = (collectionName: string) => {
    setActiveCollection(collectionName);
  };

  const toggleSidebar = () => {
    setSidebarExpanded(!sidebarExpanded);
  };



  return (
    <div className="flex flex-col h-screen bg-black matrix-overlay">
      <Header />
      
      <div className="flex flex-1 overflow-hidden">
        <CollectionPanel 
          onSelectCollection={handleSelectCollection} 
          isExpanded={sidebarExpanded}
          onToggleSidebar={toggleSidebar}
          collections={collectionsWithFiles}
          onCollectionsChange={refetchCollections}
        />
        <ChatInterface 
          activeCollection={activeCollection} 
          collections={collectionsWithFiles.map(c => c.name)} 
          onSelectCollection={handleSelectCollection}
          onToggleSidebar={toggleSidebar}
          collectionsWithFiles={collectionsWithFiles}
        />
      </div>
    </div>
  );
};

export default Home;
