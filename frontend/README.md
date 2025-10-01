# Frontend Application

## Overview
This is a web frontend application that interfaces with the PgVector API server. The project supports both development and production deployment modes, with TypeScript for development and compiled JavaScript for production builds.

![Sample Frontend](./../frontend.jpg)

## Prerequisites
Before you start, make sure you have the following installed:
- [Node.js](https://nodejs.org/) (version compatible with node:23-bookworm-slim Docker image)
- [npm](https://www.npmjs.com/) (Node Package Manager)
- [Docker](https://www.docker.com/) (optional, for containerized deployment)

## Getting Started

### Initial Setup
```bash
# Navigate to the frontend directory
cd frontend

# Install dependencies
npm install
```

> **Note**: This project uses a custom npm registry. Ensure you have access to the specified registry URL.

## Development Setup

Choose one of the following methods to run the application in development mode:

### Option 1: Docker Development Environment

1. **Build and run the development container:**
    ```bash
    docker build -t frontend-dev -f Dockerfile .
    docker run -p 3000:3000 frontend-dev
    ```

### Option 2: Local Development (Without Docker)

1. **Install dependencies** (if not already done):
   ```bash
   npm install --registry=https://nexus3.spe.sony.com/repository/npm/
   ```

2. **Start development server:**
   ```bash
   npm run dev
   ```

3. **Access the application:**
   - Open your browser and navigate to `http://localhost:3000`

## Production Setup

Choose one of the following methods to deploy the application in production mode:

### Option 1: Docker Production Environment

1. **Build and run the production container:**
    ```bash
    docker build -t frontend-prod .
    docker run -p 3000:3000 frontend-prod
    ```

### Option 2: Local Production Build (Without Docker)

1. **Install dependencies for production:**
    ```bash
    npm ci
    ```
    > **Note**: `npm ci` is recommended for production as it installs from package-lock.json for reproducible builds.

2. **Build the application:**
    ```bash
    npm run build
    ```

3. **Start the production server:**
    ```bash
    npm run start
    ```

4. **Access the application:**
   - The application will be available at `http://localhost:3000`

> **Important**: Ensure all necessary environment variables are properly configured for your deployment environment before starting the production server.

## Docker Configuration

The Dockerfile in this project is configured to build and run the frontend application. Here's what each step does:

### Dockerfile Steps
- **`FROM node:23-bookworm-slim`** - Uses Node.js version 23 on a minimal Debian base image
- **`RUN apt -y update && apt -y upgrade`** - Updates the system packages for security
- **`RUN npm upgrade -g --registry=...`** - Updates npm globally using the custom registry
- **`WORKDIR /app`** - Sets the working directory inside the container
- **`ADD package.json .`** - Copies the package.json file to install dependencies first (for better layer caching)
- **`RUN npm install --registry=...`** - Installs project dependencies using the custom registry
- **`COPY . .`** - Copies all remaining project files into the container
- **`EXPOSE 3000`** - Exposes port 3000 for the web server
- **`CMD ["npm", "start"]`** - Runs the application using the start script

### Available npm Scripts
Based on the commands referenced in this README, the following npm scripts should be available:
- `npm run dev` - Start development server
- `npm run build` - Build the application for production
- `npm run start` - Start the production server

> **Note**: The exact Docker configuration and available scripts may vary based on your specific project setup. Refer to `package.json` for the complete list of available scripts.