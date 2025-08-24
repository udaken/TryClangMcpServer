# Use multi-stage build for optimization
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Copy project files for better layer caching
COPY ["TryClangMcpServer/TryClangMcpServer.csproj", "TryClangMcpServer/"]
COPY ["TryClangMcpServer.Tests/TryClangMcpServer.Tests.csproj", "TryClangMcpServer.Tests/"]

# Restore dependencies
RUN dotnet restore "TryClangMcpServer/TryClangMcpServer.csproj" \
    --runtime linux-musl-x64

# Copy source code
COPY . .

# Build and publish in one step to reduce layers
WORKDIR "/src/TryClangMcpServer"
RUN dotnet publish "TryClangMcpServer.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    --runtime linux-musl-x64 \
    --self-contained false \
    --no-cache

# Runtime stage using Alpine for smaller image
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS final

# Install minimal dependencies for ClangSharp
RUN apk add --no-cache \
    libstdc++ \
    clang15-libs \
    curl \
    && rm -rf /var/cache/apk/*

WORKDIR /app

# Copy published application
COPY --from=build /app/publish .

# Create non-root user for security
RUN addgroup -g 1000 mcpuser \
    && adduser -D -s /bin/sh -u 1000 -G mcpuser mcpuser \
    && chown -R mcpuser:mcpuser /app

# Switch to non-root user
USER mcpuser

# Expose port for HTTP mode
EXPOSE 3000

# Environment variables
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:3000 \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_USE_POLLING_FILE_WATCHER=true \
    ASPNETCORE_LOGGING__CONSOLE__DISABLECOLORS=true

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:3000/health || exit 1

# Default to HTTP mode for container deployment
ENTRYPOINT ["dotnet", "TryClangMcpServer.dll", "--http", "--port", "3000"]