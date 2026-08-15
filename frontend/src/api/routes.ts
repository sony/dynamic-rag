import type { Express } from "express";
import type { Server } from "http";
import { createServer as createHttpsServer } from "https";
import { readFileSync } from "fs";
import { storage } from "./storage";

// Define a middleware to handle API requests when running in development mode
const createMockApiMiddleware = (app: Express) => {
  // Mock handlers for schema management
  app.get('/api/read-chat-models', (req, res) => {
    res.json(['gpt-3.5-turbo', 'gpt-4']);
  });

  app.get('/api/read-embedding-models', (req, res) => {
    res.json(['text-embedding-ada-002', 'text-embedding-3-small', 'text-embedding-3-large']);
  });

  app.get('/api/read-collections', (req, res) => {
    res.json(['documents', 'research']);
  });

  app.get('/api/create-collection', (req, res) => {
    const { collectionName } = req.query;
    res.json({ 
      success: true, 
      message: `Collection "${collectionName}" created successfully`
    });
  });

  app.get('/api/delete-collection', (req, res) => {
    const { collectionName } = req.query;
    res.json({ 
      success: true, 
      message: `Collection "${collectionName}" deleted successfully`
    });
  });

  // Mock handlers for collection management
  app.post('/api/ingest-file', (req, res) => {
    const { filePath, collectionName } = req.body;
    res.json({
      success: true,
      message: `File "${filePath}" ingested into "${collectionName}" successfully`
    });
  });

  app.post('/api/remove-file-from-collection', (req, res) => {
    const { filePath, collectionName } = req.body;
    res.json({
      success: true,
      message: `File "${filePath}" removed from "${collectionName}" successfully`
    });
  });

  app.post('/api/vector-search-collection', (req, res) => {
    const { collectionName, nResults } = req.body;
    const results = generateMockSearchResults(collectionName, nResults);
    res.json(results);
  });

  app.post('/api/semantic-search-collection', (req, res) => {
    const { collectionName, nResults } = req.body;
    const results = generateMockSearchResults(collectionName, nResults);
    res.json(results);
  });

  app.post('/api/hybrid-search-collection', (req, res) => {
    const { collectionName, nResults } = req.body;
    const results = generateMockSearchResults(collectionName, nResults);
    res.json(results);
  });

  // Mock handler for context-aware chat
  app.post('/api/search-collection', (req, res) => {
    const { query, collectionName } = req.body;
    const results = generateMockSearchResults(collectionName, 5);
    
    let finalAnswer = 'Based on the documents in this collection, I found some relevant information. ';
    
    if (collectionName === 'documents') {
      finalAnswer += 'The report.pdf file contains detailed analysis of processing efficiency and memory usage. The data.csv provides structured data supporting these findings.';
    } else {
      finalAnswer += 'The paper.docx contains academic research on advanced algorithms with performance benchmarks and experimental results.';
    }
    
    res.json({
      query,
      finalAnswer,
      searchResult: results
    });
  });
};

// Helper function to generate mock search results
function generateMockSearchResults(collectionName: string, nResults: number) {
  const results = [];
  const fileNames = collectionName === 'documents' 
    ? ['report.pdf', 'data.csv'] 
    : ['paper.docx'];
    
  for (let i = 0; i < Math.min(nResults, 5); i++) {
    const fileName = fileNames[i % fileNames.length];
    results.push({
      rank: i,
      score: (0.95 - (i * 0.1)).toString(),
      fileName,
      chunkIndex: i + 1,
      definition: `This is chunk ${i + 1} from ${fileName} containing relevant information about the search query.`,
      embeddingModelName: 'text-embedding-ada-002'
    });
  }
  
  return results;
}

export async function registerRoutes(app: Express): Promise<Server> {
  // Set up API routes with /api prefix
  app.use('/api', (req, res, next) => {
    // Log API requests
    console.log(`API Request: ${req.method} ${req.path}`);
    next();
  });

  // Create mock API endpoints
  createMockApiMiddleware(app);

  const tlsCert = process.env.TLS_CERT_PATH;
  const tlsKey = process.env.TLS_KEY_PATH;

  // TLS is mandatory in every environment: there is deliberately no cleartext HTTP code path,
  // so a missing or misconfigured certificate can never silently downgrade traffic. For local
  // development run `npm run certs:dev` once to mint a self-signed pair.
  if (!tlsCert || !tlsKey) {
    throw new Error(
      "TLS_CERT_PATH and TLS_KEY_PATH must be set; refusing to start without TLS. " +
        "For local development run `npm run certs:dev`."
    );
  }

  return createHttpsServer(
    { cert: readFileSync(tlsCert), key: readFileSync(tlsKey) },
    app
  ) as unknown as Server;
}
