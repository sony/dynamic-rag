import express, { type Express } from "express";
import cors from "cors";
import rateLimit from "express-rate-limit";
import fs from "fs";
import path, { dirname } from "path";
import { fileURLToPath } from "url";
import { createServer as createViteServer, createLogger } from "vite";
import { Server } from "http";
import viteConfig from "../vite.config";
import { nanoid } from "nanoid";

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

const viteLogger = createLogger();

export function log(message: string, source = "express") {
  const formattedTime = new Date().toLocaleTimeString("en-US", {
    hour: "numeric",
    minute: "2-digit",
    second: "2-digit",
    hour12: true,
  });

  console.log(`${formattedTime} [${source}] ${message}`);
}

// Origins permitted to make credentialed cross-origin requests. A wildcard cannot be used
// together with `credentials: true`, so the list is driven by CORS_ALLOWED_ORIGINS
// (comma-separated) and falls back to the local development hosts.
const allowedOrigins = (process.env.CORS_ALLOWED_ORIGINS
  ?.split(",")
  .map((origin) => origin.trim())
  .filter(Boolean)) ?? [
  "http://localhost:3000",
  "http://localhost:5272",
];

// Throttle the SPA/static handlers below: each one touches the filesystem per request,
// so an unbounded request rate is a cheap denial-of-service vector.
const staticContentLimiter = rateLimit({ windowMs: 60 * 1000, max: 300 });

export async function setupVite(app: Express, server: Server) {
  // Apply CORS configuration
  app.use(cors({
    origin: allowedOrigins,
    methods: ['GET', 'POST', 'PUT', 'DELETE', 'OPTIONS'],
    allowedHeaders: 'Content-Type, Authorization',
    credentials: true
  }));

  const serverOptions = {
    middlewareMode: true,
    hmr: { server },
    allowedHosts: true,
  };

  const vite = await createViteServer({
    ...viteConfig,
    configFile: false,
    customLogger: {
      ...viteLogger,
      error: (msg, options) => {
        viteLogger.error(msg, options);
        process.exit(1);
      },
    },
    server: serverOptions,
    appType: "custom",
  });

  app.use(vite.middlewares);

  app.use("*", staticContentLimiter, async (req, res, next) => {
    // Skip API routes- let them be handled by the backend server
    if (req.originalUrl.startsWith("/api")) {
      return next();
    }

    try {
      // Reject URLs containing characters that could enable XSS
      if (/[<>"'`]/.test(req.originalUrl)) {
        res.status(400).end("Bad Request");
        return;
      }

      // Look for index.html in the project root
      const indexPath = path.resolve(process.cwd(), "index.html");
      
      // always reload the index.html file from disk in case it changes
      let template = await fs.promises.readFile(indexPath, "utf-8");
      template = template.replace(
        `src="./src/main.tsx"`,
        `src="./src/main.tsx?v=${nanoid()}"`,
      );
      // Use "/" as the URL context — all SPA routes serve the same index.html
      const page = await vite.transformIndexHtml("/", template);
      res.status(200).set({ "Content-Type": "text/html" }).end(page);
    } catch (e) {
      vite.ssrFixStacktrace(e as Error);
      next(e);
    }
  });
}

export function serveStatic(app: Express) {
  const buildPath = path.resolve(process.cwd(), "dist");

  if (!fs.existsSync(buildPath)) {
    throw new Error(
      `Could not find the build directory: ${buildPath}, make sure to build the client first`
    );
  }

  // Apply CORS configuration for production
  app.use(cors({
    origin: allowedOrigins,
    methods: ['GET', 'POST', 'PUT', 'DELETE', 'OPTIONS'],
    allowedHeaders: 'Content-Type, Authorization',
    credentials: true
  }));

  app.use(staticContentLimiter, express.static(buildPath));

  // Serve index.html for all non-API routes (SPA fallback)
  app.get("*", staticContentLimiter, (req, res, next) => {
    if (req.path.startsWith("/api")) {
      return next();
    }
    res.sendFile(path.join(buildPath, "index.html"));
  });
}