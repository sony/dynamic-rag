import express, { type Request, Response, NextFunction } from "express";
import { registerRoutes } from "./api/routes";
import { setupVite, serveStatic, log } from "./vite";
import cors from "cors"; // Add this import
import dotenv from 'dotenv';

dotenv.config(); // This ensures your process.env has access to variables in .env file

const app = express();
app.use(cors({ origin: "*" })); // Enable CORS for all origins
app.use(express.json());
app.use(express.urlencoded({ extended: false }));

app.get("/api/config", (_req: Request, res: Response) => {
  // console.log("Inside app.get /api/config");
  res.json({
    // list here every var you need client‐side
    VITE_API_URL: process.env.VITE_API_URL,
    VITE_OCP_APIM_SUBSCRIPTION_KEY: process.env.VITE_OCP_APIM_SUBSCRIPTION_KEY
  });
});

app.use((req, res, next) => {
  const start = Date.now();
  const path = req.path;
  let capturedJsonResponse: Record<string, any> | undefined = undefined;

  const originalResJson = res.json;
  res.json = function (bodyJson, ...args) {
    capturedJsonResponse = bodyJson;
    return originalResJson.apply(res, [bodyJson, ...args]);
  };

  res.on("finish", () => {
    const duration = Date.now() - start;
    if (path.startsWith("/api")) {
      let logLine = `${req.method} ${path} ${res.statusCode} in ${duration}ms`;
      if (capturedJsonResponse) {
        logLine += ` :: ${JSON.stringify(capturedJsonResponse)}`;
      }

      if (logLine.length > 80) {
        logLine = logLine.slice(0, 79) + "…";
      }

      log(logLine);
    }
  });

  next();
});

(async () => {
  const server = await registerRoutes(app);

  app.use((err: any, _req: Request, res: Response, _next: NextFunction) => {
    const status = err.status || err.statusCode || 500;
    const message = err.message || "Internal Server Error";
    res.status(status).json({ message });
    throw err;
  });

  // importantly only setup vite in development and after
  // setting up all the other routes so the catch-all route
  // doesn't interfere with the other routes
  if (app.get("env") === "development") {
    await setupVite(app, server);
  } else {
    serveStatic(app);
  }

  // ALWAYS serve the app on port 3000
  // this serves both the API and the client.
  // It is the only port that is not firewalled.
  const port = 3000;
  const host = '0.0.0.0';  // Try using '127.0.0.1' instead of '0.0.0.0'

  server.listen(port, host, () => {
    log(`frontend server running on http://${host}:${port}`);
  });
})();
