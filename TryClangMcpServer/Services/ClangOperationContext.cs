using ClangSharp.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TryClangMcpServer.Configuration;

namespace TryClangMcpServer.Services;

/// <summary>
/// Manages resources for Clang operations with proper disposal
/// </summary>
public sealed class ClangOperationContext : IAsyncDisposable
{
    private CXIndex? _index;
    private CXTranslationUnit? _translationUnit;
    private readonly string _tempDirectory;
    private readonly ILogger _logger;
    private readonly ClangOptions _options;
    private bool _disposed = false;

    public ClangOperationContext(string tempDirectory, ILogger logger, IOptions<ClangOptions> options)
    {
        _tempDirectory = tempDirectory;
        _logger = logger;
        _options = options.Value;
        _index = CXIndex.Create();
    }

    public CXIndex Index => _index ?? throw new ObjectDisposedException(nameof(ClangOperationContext));

    public CXTranslationUnit? TranslationUnit
    {
        get => _translationUnit;
        set
        {
            _translationUnit?.Dispose();
            _translationUnit = value;
        }
    }

    public string TempDirectory => _tempDirectory;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        try
        {
            _translationUnit?.Dispose();
            _index?.Dispose();

            await CleanupDirectoryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during ClangOperationContext disposal");
        }
        finally
        {
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    private async ValueTask CleanupDirectoryAsync()
    {
        if (!Directory.Exists(_tempDirectory))
            return;

        for (int attempts = 0; attempts < _options.CleanupRetryAttempts; attempts++)
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
                _logger.LogDebug("Successfully cleaned up temporary directory: {Directory}", _tempDirectory);
                return;
            }
            catch (IOException ex) when (attempts < _options.CleanupRetryAttempts - 1)
            {
                _logger.LogDebug(ex, "Failed to cleanup directory (attempt {Attempt}/{Total}), retrying...",
                    attempts + 1, _options.CleanupRetryAttempts);
                await Task.Delay(_options.CleanupDelayMs);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup temporary directory: {Directory}", _tempDirectory);
                break;
            }
        }

        _logger.LogWarning("Failed to cleanup temporary directory after {Attempts} attempts: {Directory}",
            _options.CleanupRetryAttempts, _tempDirectory);
    }

    ~ClangOperationContext()
    {
        if (!_disposed)
        {
            _logger.LogWarning("ClangOperationContext was not properly disposed");
        }
    }
}