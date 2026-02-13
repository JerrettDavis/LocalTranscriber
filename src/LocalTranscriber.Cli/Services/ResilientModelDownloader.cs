using LocalTranscriber.Cli.Services.Mirrors;
using Whisper.net.Ggml;

namespace LocalTranscriber.Cli.Services;

/// <summary>
/// Downloads Whisper GGML models with retry logic, multi-mirror support, and enterprise network support.
/// </summary>
internal static class ResilientModelDownloader
{
    /// <summary>
    /// Model download options.
    /// </summary>
    public sealed record DownloadOptions(
        string? MirrorName = null,
        string? MirrorUrl = null,
        bool TrustAllCerts = false);

    /// <summary>
    /// Gets or downloads the Whisper model, with automatic mirror fallback.
    /// </summary>
    public static Task<string> EnsureModelAsync(
        GgmlType type,
        bool trustAllCerts = false,
        CancellationToken ct = default)
        => EnsureModelAsync(type, new DownloadOptions(TrustAllCerts: trustAllCerts), ct);

    /// <summary>
    /// Gets or downloads the Whisper model with configurable mirror options.
    /// </summary>
    public static async Task<string> EnsureModelAsync(
        GgmlType type,
        DownloadOptions options,
        CancellationToken ct = default)
    {
        var modelFileName = GetModelFilename(type);
        var modelDir = GetModelDirectory();
        var modelPath = Path.Combine(modelDir, modelFileName);

        if (File.Exists(modelPath))
        {
            var fileInfo = new FileInfo(modelPath);
            if (fileInfo.Length > 1024 * 1024) // > 1MB = likely valid
            {
                Console.WriteLine($"Using cached model: {modelPath}");
                return modelPath;
            }
            
            // Corrupted/incomplete download, remove and retry
            Console.WriteLine($"Removing incomplete model file: {modelPath}");
            File.Delete(modelPath);
        }

        Directory.CreateDirectory(modelDir);
        
        Console.WriteLine($"Downloading Whisper model '{type}'...");
        Console.WriteLine($"  Model: {modelFileName}");
        Console.WriteLine($"  Destination: {modelPath}");
        
        if (options.TrustAllCerts)
            Console.WriteLine("  [WARN] SSL validation disabled");

        var progress = new Progress<double>(p =>
        {
            var percent = p * 100;
            Console.Write($"\r  Progress: {percent:F1}%   ");
        });

        var resolver = new MirrorResolver(options.MirrorUrl);

        try
        {
            // If a specific mirror is requested, try only that one
            if (!string.IsNullOrEmpty(options.MirrorName))
            {
                var mirror = resolver.GetByName(options.MirrorName);
                if (mirror == null)
                {
                    var available = string.Join(", ", resolver.GetMirrorNames());
                    throw new ArgumentException(
                        $"Unknown mirror '{options.MirrorName}'. Available: {available}");
                }
                
                var url = mirror.GetDownloadUrl(modelFileName);
                Console.WriteLine($"Using mirror: {mirror.Name}");
                Console.WriteLine($"  URL: {url}");
                
                await ResilientHttp.DownloadFileAsync(url, modelPath, options.TrustAllCerts, progress, ct);
                Console.WriteLine();
                Console.WriteLine($"Model downloaded successfully from {mirror.Name}.");
            }
            else
            {
                // Auto-fallback through all mirrors
                var result = await resolver.DownloadWithFallbackAsync(
                    modelFileName, modelPath, options.TrustAllCerts, progress, ct);
                
                Console.WriteLine(); // New line after progress
                
                if (result.HasValue)
                {
                    Console.WriteLine($"Model downloaded successfully from {result.Value.Mirror.Name}.");
                }
            }
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine(); // New line after progress
            Console.WriteLine($"\n[ERROR] Model download failed: {ex.Message}");
            Console.WriteLine();
            Console.WriteLine("Manual download instructions:");
            Console.WriteLine($"  1. Download {modelFileName} from any available source");
            Console.WriteLine($"  2. Save to: {modelPath}");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --mirror-url <url>    Use a custom mirror URL");
            Console.WriteLine("  --mirror <name>       Use a specific mirror (run 'mirrors' to list)");
            Console.WriteLine();
            throw;
        }

        return modelPath;
    }
    
    /// <summary>
    /// Prints status of all configured mirrors.
    /// </summary>
    public static void PrintMirrorStatus(string? customUrl = null)
    {
        var resolver = new MirrorResolver(customUrl);
        resolver.PrintMirrorStatus();
    }
    
    /// <summary>
    /// Gets available mirror names.
    /// </summary>
    public static IReadOnlyList<string> GetMirrorNames(string? customUrl = null)
    {
        var resolver = new MirrorResolver(customUrl);
        return resolver.GetMirrorNames();
    }

    /// <summary>
    /// Gets the local model directory path.
    /// </summary>
    public static string GetModelDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LocalTranscriber",
            "models");
    }

    /// <summary>
    /// Gets the filename for a model type.
    /// </summary>
    public static string GetModelFilename(GgmlType type)
    {
        return type switch
        {
            GgmlType.Tiny => "ggml-tiny.bin",
            GgmlType.TinyEn => "ggml-tiny.en.bin",
            GgmlType.Base => "ggml-base.bin",
            GgmlType.BaseEn => "ggml-base.en.bin",
            GgmlType.Small => "ggml-small.bin",
            GgmlType.SmallEn => "ggml-small.en.bin",
            GgmlType.Medium => "ggml-medium.bin",
            GgmlType.MediumEn => "ggml-medium.en.bin",
            GgmlType.LargeV1 => "ggml-large-v1.bin",
            GgmlType.LargeV2 => "ggml-large-v2.bin",
            GgmlType.LargeV3 => "ggml-large-v3.bin",
            GgmlType.LargeV3Turbo => "ggml-large-v3-turbo.bin",
            _ => $"ggml-{type.ToString().ToLowerInvariant()}.bin"
        };
    }

    /// <summary>
    /// Lists available cached models.
    /// </summary>
    public static IEnumerable<(GgmlType Type, string Path, long SizeMB)> ListCachedModels()
    {
        var modelDir = GetModelDirectory();
        if (!Directory.Exists(modelDir))
            yield break;

        foreach (var type in Enum.GetValues<GgmlType>())
        {
            var filename = GetModelFilename(type);
            var path = Path.Combine(modelDir, filename);
            
            if (File.Exists(path))
            {
                var size = new FileInfo(path).Length / (1024 * 1024);
                yield return (type, path, size);
            }
        }
    }
}
