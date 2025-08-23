# TryClangMcpServer

A Model Context Protocol (MCP) server that provides C/C++ code compilation, static analysis, and AST generation capabilities using the LLVM/Clang toolchain via ClangSharp.

## Features

This MCP server implements three powerful tools for C/C++ development:

- **`compile_cpp`** - Compiles C/C++ code with various compiler options and returns detailed diagnostics
- **`analyze_cpp`** - Performs static analysis using Clang Static Analyzer to find potential issues
- **`get_ast`** - Generates Abstract Syntax Trees in JSON format for code analysis

## Prerequisites

- .NET 9.0 or later
- Supported platforms: Windows (x64), Linux (x64), macOS (x64/ARM64)
- Appropriate libclang native dependencies for your platform

## Installation

1. Clone the repository:
```bash
git clone <repository-url>
cd TryClangMcpServer
```

2. Restore dependencies:
```bash
dotnet restore
```

3. Build the project:
```bash
dotnet build
```

## Usage

### Running the MCP Server

#### Stdio Mode (Default)
Start the server using stdio transport:
```bash
dotnet run
```

#### HTTP Mode
Start the server in HTTP mode for web-based access:
```bash
# Default port 3000
dotnet run --http

# Custom port
dotnet run --http --port 8080
```

### Testing the Server

#### Stdio Mode Testing
Test basic functionality:
```bash
echo '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' | dotnet run
```

#### HTTP Mode Testing
Test health endpoint:
```bash
curl http://localhost:3000/health
```

List available tools:
```bash
curl -X POST http://localhost:3000/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

Call a tool:
```bash
curl -X POST http://localhost:3000/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
      "name": "compile_cpp",
      "arguments": {
        "sourceCode": "int main() { return 0; }"
      }
    }
  }'
```

### Linux-specific Usage

The server works identically on Linux systems:

```bash
# On Linux: Start in stdio mode
./TryClangMcpServer

# On Linux: Start in HTTP mode
./TryClangMcpServer --http --port 3000

# Test on Linux using curl
curl -X POST http://localhost:3000/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
      "name": "compile_cpp",
      "arguments": {
        "sourceCode": "#include <iostream>\nint main() { std::cout << \"Hello Linux!\" << std::endl; return 0; }",
        "options": "-std=c++17 -Wall"
      }
    }
  }'
```

### MCP Tools

#### compile_cpp
Compiles C/C++ source code and returns compilation diagnostics.

**Parameters:**
- `sourceCode` (string) - The C/C++ source code to compile
- `options` (string, optional) - Compiler options (e.g., "-std=c++17 -Wall")

**Example Response:**
```json
{
  "success": true,
  "errors": 0,
  "warnings": 1,
  "diagnostics": [
    {
      "severity": "Warning",
      "message": "unused variable 'x'",
      "file": "source.cpp",
      "line": 5,
      "column": 9
    }
  ],
  "sourceFile": "source.cpp"
}
```

#### analyze_cpp
Performs static analysis on C/C++ code to identify potential issues.

**Parameters:**
- `sourceCode` (string) - The C/C++ source code to analyze
- `options` (string, optional) - Analysis options

**Example Response:**
```json
{
  "success": true,
  "issues": 2,
  "findings": [
    {
      "severity": "Warning",
      "message": "Potential memory leak",
      "file": "source.cpp",
      "line": 10,
      "column": 5
    }
  ],
  "sourceFile": "source.cpp"
}
```

#### get_ast
Generates an Abstract Syntax Tree representation of the C/C++ code.

**Parameters:**
- `sourceCode` (string) - The C/C++ source code to parse
- `options` (string, optional) - Parser options

**Example Response:**
```json
{
  "success": true,
  "sourceFile": "source.cpp",
  "ast": [
    {
      "kind": "FunctionDecl",
      "spelling": "main",
      "line": 3,
      "column": 5,
      "children": [...]
    }
  ]
}
```

## Development

### Project Structure

```
TryClangMcpServer/
├── TryClangMcpServer/           # Main MCP server implementation
│   ├── Program.cs               # Server entry point and tool implementations
│   └── TryClangMcpServer.csproj # Project configuration
├── TryClangMcpServer.Tests/     # Unit and integration tests
│   ├── UnitTest1.cs             # Test implementations
│   └── TryClangMcpServer.Tests.csproj
├── CLAUDE.md                    # Development guidance
└── README.md                    # This file
```

### Building and Testing

```bash
# Build the solution (cross-platform)
dotnet build

# Build for specific platform
dotnet build --runtime win-x64
dotnet build --runtime linux-x64
dotnet build --runtime osx-x64
dotnet build --runtime osx-arm64

# Run tests (requires libclang native libraries for your platform)
dotnet test

# Build for release
dotnet build --configuration Release

# Publish self-contained executable for specific platform
dotnet publish --configuration Release --runtime linux-x64 --self-contained
dotnet publish --configuration Release --runtime win-x64 --self-contained
```

### Architecture

The server is built using:

- **ModelContextProtocol SDK** for MCP server implementation
- **ASP.NET Core** for HTTP mode support
- **ClangSharp 20.1.2** for LLVM/Clang integration
- **Microsoft.Extensions.Hosting** for service hosting
- **NUnit** for testing

Key architectural features:

- **Dual Transport Support**: Both stdio and HTTP transports supported
- **Isolated Processing**: Each compilation uses temporary directories with GUID-based names
- **Automatic Cleanup**: Temporary files and artifacts are automatically cleaned up with retry logic
- **Comprehensive Error Handling**: All errors are caught and returned in consistent JSON format
- **Thread-Safe Operations**: Concurrent tool invocations are safely handled
- **Input Validation**: Security-focused validation prevents dangerous operations
- **Detailed Logging**: Full logging support via Microsoft.Extensions.Logging
- **CORS Support**: Cross-origin requests enabled for HTTP mode

## Dependencies

### Runtime Dependencies
- ClangSharp 20.1.2
- ClangSharp.Interop 20.1.2
- libclang 20.1.2
- libclang.runtime.win-x64 20.1.2 (Windows x64)
- libclang.runtime.linux-x64 20.1.2 (Linux x64)
- libclang.runtime.osx-x64 20.1.2 (macOS x64)
- libclang.runtime.osx-arm64 20.1.2 (macOS ARM64)
- ModelContextProtocol 0.3.0-preview.4
- Microsoft.Extensions.Hosting 9.0.8

### Development Dependencies
- NUnit 4.2.2
- Microsoft.NET.Test.Sdk 17.12.0
- NUnit3TestAdapter 4.6.0

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for new functionality
5. Ensure all tests pass
6. Submit a pull request

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Support

For issues and questions:
- Open an issue on GitHub
- Check the CLAUDE.md file for development guidance

## Limitations

- Requires appropriate libclang runtime libraries for the target platform
- Static analysis features depend on Clang Static Analyzer capabilities
- Some compiler-specific features may vary between platforms (Windows/Linux/macOS)
- Resource limits and security filtering may require adjustment for different environments