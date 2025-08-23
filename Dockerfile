# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files
COPY ["TryClangMcpServer/TryClangMcpServer.csproj", "TryClangMcpServer/"]
COPY ["TryClangMcpServer.Tests/TryClangMcpServer.Tests.csproj", "TryClangMcpServer.Tests/"]

# Restore dependencies
RUN dotnet restore "TryClangMcpServer/TryClangMcpServer.csproj"

# Copy source code
COPY . .

# Build the application
WORKDIR "/src/TryClangMcpServer"
RUN dotnet build "TryClangMcpServer.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "TryClangMcpServer.csproj" -c Release -o /app/publish --no-restore --runtime linux-x64 --self-contained false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Install dependencies for libclang (required for ClangSharp)
RUN apt-get update && apt-get install -y \
    libc6-dev \
    && rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=publish /app/publish .

# Create non-root user for security
RUN groupadd -r mcpuser && useradd -r -g mcpuser mcpuser
RUN chown -R mcpuser:mcpuser /app
USER mcpuser

# Expose port for HTTP mode
EXPOSE 3000

# Environment variables
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:3000

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:3000/health || exit 1

# Default to HTTP mode for container deployment
ENTRYPOINT ["dotnet", "TryClangMcpServer.dll", "--http", "--port", "3000"]