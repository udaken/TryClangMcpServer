# Docker Deployment Guide

## Docker Compose Usage

### HTTP Mode (Default)

Start the server in HTTP mode:
```bash
docker-compose up -d
```

The server will be available at `http://localhost:3000`

### Development Mode

For development with volume mounting and different port:
```bash
docker-compose -f docker-compose.yml -f docker-compose.override.yml up -d
```

The server will be available at `http://localhost:3001`

### Stdio Mode

For MCP client integration using stdio transport:
```bash
docker-compose --profile stdio run --rm tryclangmcpserver-stdio
```

### Stop Services

```bash
docker-compose down
```

## Available Commands

### Build and Start
```bash
# Build and start in detached mode
docker-compose up -d --build

# View logs
docker-compose logs -f

# Check service status
docker-compose ps
```

### Testing

```bash
# Health check
curl http://localhost:3000/health

# List available MCP tools
curl -X POST http://localhost:3000/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'

# Test C++ compilation
curl -X POST http://localhost:3000/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
      "name": "compile_cpp",
      "arguments": {
        "sourceCode": "#include <iostream>\nint main() { std::cout << \"Hello Docker!\" << std::endl; return 0; }",
        "options": "-std=c++17"
      }
    }
  }'
```

## Configuration

### Environment Variables

- `ASPNETCORE_ENVIRONMENT`: Set to `Development` or `Production`
- `ASPNETCORE_URLS`: HTTP binding URLs (default: `http://+:3000`)

### Volumes

Development mode mounts `./logs` for log file access.

### Profiles

- `default`: HTTP mode services
- `stdio`: Stdio mode for MCP client integration

## Troubleshooting

### Check container logs
```bash
docker-compose logs tryclangmcpserver
```

### Restart services
```bash
docker-compose restart
```

### Remove and rebuild
```bash
docker-compose down
docker-compose up -d --build
```

### Access container shell
```bash
docker-compose exec tryclangmcpserver /bin/bash
```