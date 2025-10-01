# Stage 1: Build the Application
FROM node:23-bookworm-slim AS builder

# Update and install dependencies if needed
RUN apt -y update && apt -y upgrade

WORKDIR /app

# Copy package files and install dependencies
COPY package.json package-lock.json* ./
RUN npm install

# Copy the rest of the source code
COPY . .

# Rebuild esbuild to ensure the binary is compatible with Linux
RUN npm rebuild esbuild

# Run the build command to generate /app/dist
RUN npm run build

# Stage 2: Prepare the Final Image for Production
FROM node:23-bookworm-slim

WORKDIR /app

# Copy the built application from the builder stage
COPY --from=builder /app /app

# Set environment variable for production
ENV NODE_ENV=production

# Expose the port if needed (e.g., 3000)
EXPOSE 3000

# Start the application
CMD ["npm", "start"]