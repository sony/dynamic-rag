import { createContext, useContext, ReactNode } from 'react';
import { ApiClient } from '@/lib/api';

interface EnvironmentContextType {
  apiClient: ApiClient;
}

const EnvironmentContext = createContext<EnvironmentContextType | undefined>(undefined);

// Determine if in development mode from Vite's environment variable
const isDevelopmentMode = import.meta.env.MODE === 'development';

export const EnvironmentProvider = ({ children }: { children: ReactNode }) => {
  // Create API client with development mode based on the environment
  const apiClient = new ApiClient(isDevelopmentMode);

  return (
    <EnvironmentContext.Provider value={{ apiClient }}>
      {children}
    </EnvironmentContext.Provider>
  );
};

export const useEnvironment = () => {
  const context = useContext(EnvironmentContext);
  if (context === undefined) {
    throw new Error('useEnvironment must be used within an EnvironmentProvider');
  }
  return context;
};