namespace LocalTranscriber.Cli.Services.Mirrors;

/// <summary>
/// Resolves and manages model download mirrors with automatic fallback.
/// </summary>
internal sealed class MirrorResolver
{
    private readonly List<IModelMirror> _mirrors;
    
    public MirrorResolver(string? customUrl = null)
    {
        _mirrors =
        [
            new HuggingFaceMirror(),    // Priority 10 - primary for local use
            new HuggingFaceMirrorCN(),  // Priority 15 - HF community mirror
            new ModelScopeMirror(),     // Priority 20 - Alibaba mirror
            new GitHubReleasesMirror(), // Priority 5 in GitHub Actions, 25 otherwise
        ];
        
        // Add custom URL mirror if provided (via flag or env var)
        var envUrl = Environment.GetEnvironmentVariable("WHISPER_MODEL_MIRROR")?.TrimEnd('/');
        var effectiveCustomUrl = customUrl?.TrimEnd('/') ?? envUrl;
        
        if (!string.IsNullOrEmpty(effectiveCustomUrl))
        {
            _mirrors.Insert(0, new CustomUrlMirror(effectiveCustomUrl));
        }
    }
    
    /// <summary>
    /// Gets a mirror by name (case-insensitive).
    /// </summary>
    public IModelMirror? GetByName(string name)
    {
        return _mirrors.FirstOrDefault(m => 
            m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// Gets all registered mirror names.
    /// </summary>
    public IReadOnlyList<string> GetMirrorNames()
    {
        return _mirrors.Select(m => m.Name).ToList();
    }
    
    /// <summary>
    /// Gets all enabled mirrors sorted by priority.
    /// </summary>
    public IReadOnlyList<IModelMirror> GetEnabledMirrors()
    {
        return _mirrors
            .Where(m => m.IsEnabled)
            .OrderBy(m => m.Priority)
            .ToList();
    }
    
    /// <summary>
    /// Probes all mirrors in parallel and returns available ones sorted by priority.
    /// </summary>
    public async Task<IReadOnlyList<IModelMirror>> ProbeAllAsync(
        string modelFileName,
        CancellationToken ct = default)
    {
        var enabledMirrors = GetEnabledMirrors();
        var probeTasks = enabledMirrors.Select(async mirror =>
        {
            var available = await mirror.ProbeAsync(modelFileName, ct);
            return (mirror, available);
        });
        
        var results = await Task.WhenAll(probeTasks);
        
        return results
            .Where(r => r.available)
            .Select(r => r.mirror)
            .OrderBy(m => m.Priority)
            .ToList();
    }
    
    /// <summary>
    /// Finds the first available mirror (probes sequentially by priority).
    /// </summary>
    public async Task<IModelMirror?> FindFirstAvailableAsync(
        string modelFileName,
        CancellationToken ct = default)
    {
        foreach (var mirror in GetEnabledMirrors())
        {
            Console.WriteLine($"  Probing {mirror.Name}...");
            
            if (await mirror.ProbeAsync(modelFileName, ct))
            {
                Console.WriteLine($"  ✓ {mirror.Name} available");
                return mirror;
            }
            
            Console.WriteLine($"  ✗ {mirror.Name} unavailable");
        }
        
        return null;
    }
    
    /// <summary>
    /// Attempts to download from mirrors in priority order until one succeeds.
    /// </summary>
    public async Task<(IModelMirror Mirror, string Url)?> DownloadWithFallbackAsync(
        string modelFileName,
        string destinationPath,
        bool trustAllCerts = false,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var mirrors = GetEnabledMirrors();
        var errors = new List<(string Mirror, string Error)>();
        
        foreach (var mirror in mirrors)
        {
            var url = mirror.GetDownloadUrl(modelFileName);
            Console.WriteLine($"Trying {mirror.Name}: {url}");
            
            try
            {
                await ResilientHttp.DownloadFileAsync(url, destinationPath, trustAllCerts, progress, ct);
                return (mirror, url);
            }
            catch (HttpRequestException ex)
            {
                errors.Add((mirror.Name, ex.Message));
                Console.WriteLine($"  ✗ {mirror.Name} failed: {ex.Message}");
                
                // Clean up partial download
                var tempPath = destinationPath + ".tmp";
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
        
        // All mirrors failed - throw with summary
        var errorSummary = string.Join("\n", errors.Select(e => $"  - {e.Mirror}: {e.Error}"));
        throw new HttpRequestException(
            $"All mirrors failed for {modelFileName}:\n{errorSummary}\n\n" +
            "Options:\n" +
            "- Set WHISPER_MODEL_MIRROR environment variable to a custom mirror URL\n" +
            "- Manually download the model and place it in the models directory\n" +
            "- Check network/firewall settings");
    }
    
    /// <summary>
    /// Lists all configured mirrors and their status.
    /// </summary>
    public void PrintMirrorStatus()
    {
        Console.WriteLine("Configured model mirrors:");
        foreach (var mirror in _mirrors.OrderBy(m => m.Priority))
        {
            var status = mirror.IsEnabled ? "enabled" : "disabled";
            Console.WriteLine($"  [{mirror.Priority:D2}] {mirror.Name}: {status}");
        }
    }
}
