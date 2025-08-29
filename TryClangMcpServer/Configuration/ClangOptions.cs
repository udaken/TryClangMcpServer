using System.ComponentModel.DataAnnotations;

namespace TryClangMcpServer.Configuration;

/// <summary>
/// Configuration options for Clang operations
/// </summary>
public class ClangOptions
{
    public const string SectionName = "Clang";

    [Range(1, 50)]
    public int MaxAstDepth { get; set; } = 10;

    [Required]
    [MinLength(1)]
    public string DefaultSourceFileName { get; set; } = "source.cpp";

    [Range(1, 10)]
    public int CleanupRetryAttempts { get; set; } = 3;

    [Range(50, 5000)]
    public int CleanupDelayMs { get; set; } = 100;

    [Range(1000, 10_000_000)] // 1KB to 10MB
    public int MaxSourceCodeSizeBytes { get; set; } = 1_000_000; // 1MB

    // Security and resource limits
    [Range(1, 20)]
    public int MaxConcurrentOperations { get; set; } = 5;

    [Range(5000, 300_000)] // 5 seconds to 5 minutes
    public int OperationTimeoutMs { get; set; } = 30_000; // 30 seconds

    [Range(1, 3600)] // 1 to 3600 requests per minute
    public int RateLimitRequestsPerMinute { get; set; } = 60;

    [Range(10, 100_000)] // 10 to 100,000 requests per hour
    public int MaxRequestsPerHour { get; set; } = 1000;

    public string[] DangerousOptions { get; set; } =
    {
        // File system access
        "-o", "--output", "-include", "-I", "--include-directory",
        "--sysroot", "-isysroot", "-working-directory",
        
        // System information leakage
        "-march=native", "-mcpu=native", "-mtune=native",
        "-pipe", "-v", "--verbose",
        
        // Preprocessor and dependency generation (can reveal system info)
        "-M", "-MD", "-MM", "-MF", "-MP", "-MT", "-MQ", "-MMD",
        
        // Temporary file handling
        "-save-temps", "--save-temps", "-save-temps=",
        
        // Target and architecture specification
        "-target", "--target", "-mfloat-abi", "-mfpu",
        
        // External tools and scripts
        "-x", "--language", "-Xclang", "-Xpreprocessor", "-Xlinker", "-Xassembler",
        
        // Debug and profiling (can leak info)
        "-g", "-gdwarf", "-glldb", "-gsce", "-gcodeview",
        "-pg", "--coverage", "-fprofile",
        
        // Linker options
        "-l", "-L", "--library", "--library-path",
        "-Wl,", "-Xlinker",
        
        // Plugin loading
        "-fplugin", "-load",
        
        // External command execution
        "-B", "--prefix", "-specs",
        
        // Linux-specific dangerous options
        "-rpath", "--rpath", "-soname", "--soname",
        "-shared", "--shared", "-static", "--static",
        "-pie", "-no-pie", "-fpic", "-fPIC", "-fpie", "-fPIE",
        "-rdynamic", "--export-dynamic",
        
        // GCC/Linux specific system access
        "-print-prog-name", "-print-file-name", "-print-libgcc-file-name",
        "-print-search-dirs", "-print-multi-directory", "-print-multi-lib",
        "-print-sysroot", "-print-sysroot-headers-suffix"
    };
}