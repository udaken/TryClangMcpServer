namespace TryClangMcpServer.Constants;

public static class JsonRpcConstants
{
    public const string Version = "2.0";

    // Method names
    public const string ToolsListMethod = "tools/list";
    public const string ToolsCallMethod = "tools/call";

    // Tool names
    public const string CompileCppTool = "compile_cpp";
    public const string AnalyzeCppTool = "analyze_cpp";
    public const string GetAstTool = "get_ast";

    // Error codes
    public static class ErrorCodes
    {
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;
        public const int RateLimitExceeded = -32099; // Custom
    }

    // Property names
    public static class Properties
    {
        public const string JsonRpc = "jsonrpc";
        public const string Method = "method";
        public const string Params = "params";
        public const string Id = "id";
        public const string Name = "name";
        public const string Arguments = "arguments";
        public const string SourceCode = "sourceCode";
        public const string Options = "options";
    }
}