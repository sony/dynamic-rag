# Start with the official .NET 9.0 SDK image based on Debian
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["server.csproj", "./"]
# RUN dotnet restore
# RUN dotnet restore -s https://nexus3.spe.sony.com/repository/nuget-proxy-v3/index.json

# Copy the rest of the source code
COPY . .

# Build the application
RUN dotnet build -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Generate XML documentation for Swagger
RUN dotnet build -c Release /p:GenerateDocumentationFile=true

# Final image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Install PostgreSQL client and SkiaSharp dependencies
RUN apt-get update && apt-get install -y \
    curl \
    postgresql-client \
    libfontconfig1 \
    libfreetype6 \
    libicu-dev \
    libpng-dev \
    libjpeg-dev \
    libgif-dev \
    libwebp-dev \
    libharfbuzz-dev \
    && rm -rf /var/lib/apt/lists/*

# # TODO: remove before flight
# # Install Azure CLI for Azure authentication
# RUN apt-get update && \
#     apt-get install -y ca-certificates curl apt-transport-https lsb-release gnupg && \
#     curl -sL https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor | tee /etc/apt/trusted.gpg.d/microsoft.gpg > /dev/null && \
#     echo "deb [arch=amd64] https://packages.microsoft.com/repos/azure-cli/ $(lsb_release -cs) main" | tee /etc/apt/sources.list.d/azure-cli.list && \
#     apt-get update && \
#     apt-get install -y azure-cli && \
#     rm -rf /var/lib/apt/lists/*
# # Note: az login will need to be run at container startup or in your entrypoint script
# RUN az login --use-device-code  # This can't be in the Dockerfile as it's interactive

# Copy the published application
COPY --from=publish /app/publish .

RUN touch .env

# Copy .env file if it exists (will be ignored if not present at build time)
COPY .env* ./

# Set environment variables (these can be overridden at runtime)
# Let the PORT environment variable control the port
# ENV ASPNETCORE_URLS=http://+:5272  # Removed to avoid conflict with Program.cs UseUrls
ENV ASPNETCORE_ENVIRONMENT=Production

# Create a directory for any potential file uploads
RUN mkdir -p /app/uploads && chmod 777 /app/uploads

# Expose port 8080 that the app will run on
EXPOSE 5272
# EXPOSE 8080

# Create an entrypoint script to handle environment variables
RUN echo '#!/bin/bash\n\
# Load .env file if it exists\n\
if [ -f ".env" ]; then\n\
  echo "Loading environment variables from .env file"\n\
  export $(grep -v "^#" .env | xargs)\n\
fi\n\
# Run the application\n\
exec dotnet server.dll "$@"\n\
' > /app/entrypoint.sh && chmod +x /app/entrypoint.sh

# ENTRYPOINT ["dotnet", "server.dll"]
# Use the entrypoint script
ENTRYPOINT ["/app/entrypoint.sh"]