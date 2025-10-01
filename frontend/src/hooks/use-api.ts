import { ApiClient } from '@/lib/api';

// Determine if in development mode from Vite's environment variable
const isDevelopmentMode = import.meta.env.MODE === 'development';

// Create and export a single API client instance
export const apiClient = new ApiClient(isDevelopmentMode);

/**
 * Hook to access the API client
 */
export const useApi = () => {
  return { apiClient };
};