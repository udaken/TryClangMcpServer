# TryClangMcpServer

A Model Context Protocol (MCP) server that provides C/C++ code compilation, static analysis, and AST generation capabilities using the LLVM/Clang toolchain via ClangSharp.

## Features

This MCP server implements three powerful tools for C/C++ development:

- **`compile_cpp`** - Compiles C/C++ code with various compiler options and returns detailed diagnostics
- **`analyze_cpp`** - Performs static analysis using Clang Static Analyzer to find potential issues
- **`get_ast`** - Generates Abstract Syntax Trees in JSON format for code analysis

## Prerequisites

- .NET 9.0 or later
- Windows x64 (for libclang native dependencies)

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

Start the server using stdio transport:
```bash
dotnet run
```

### Testing the Server

Test basic functionality:
```bash
echo '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' | dotnet run
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
# Build the solution
dotnet build

# Run tests (requires libclang native libraries)
dotnet test

# Build for release
dotnet build --configuration Release
```

### Architecture

The server is built using:

- **ModelContextProtocol SDK** for MCP server implementation
- **ClangSharp 20.1.2** for LLVM/Clang integration
- **Microsoft.Extensions.Hosting** for service hosting
- **NUnit** for testing

Key architectural features:

- **Isolated Processing**: Each compilation uses temporary directories with GUID-based names
- **Automatic Cleanup**: Temporary files and artifacts are automatically cleaned up
- **Comprehensive Error Handling**: All errors are caught and returned in consistent JSON format
- **Thread-Safe Operations**: Concurrent tool invocations are safely handled
- **Detailed Logging**: Full logging support via Microsoft.Extensions.Logging

## Dependencies

### Runtime Dependencies
- ClangSharp 20.1.2
- ClangSharp.Interop 20.1.2
- libclang 20.1.2
- libclang.runtime.win-x64 20.1.2 (Windows)
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

- Currently optimized for Windows x64 due to libclang native dependencies
- Requires appropriate libclang runtime libraries to be available
- Static analysis features depend on Clang Static Analyzer capabilities