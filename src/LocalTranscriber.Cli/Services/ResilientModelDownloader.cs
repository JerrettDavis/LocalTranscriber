using Whisper.net.Ggml;

namespace LocalTranscriber.Cli.Services;

/// <summary>
/// Downloads Whisper GGML models with retry logic and enterprise network support.
/// </summary>
internal static class ResilientModelDownloader
{
    private const string HuggingFaceBaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main";

    /// <summary>
    /// Gets or downloads the Whisper model, with retry and proxy support.
    /// </summary>
    public static async Task<string> EnsureModelAsync(
        GgmlType type,
        bool trustAllCerts = false,
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
        var url = $"{HuggingFaceBaseUrl}/{modelFileName}";
        
        Console.WriteLine($"Downloading Whisper model '{type}'...");
        Console.WriteLine($"  URL: {url}");
        Console.WriteLine($"  Destination: {modelPath}");
        
        if (trustAllCerts)
            Console.WriteLine("  [WARN] SSL validation disabled");

        var progress = new Progress<double>(p =>
        {
            var percent = p * 100;
            Console.Write($"\r  Progress: {percent:F1}%   ");
        });

        try
        {
            await ResilientHttp.DownloadFileAsync(url, modelPath, trustAllCerts, progress, ct);
            Console.WriteLine(); // New line after progress
            Console.WriteLine($"Model downloaded successfully.");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine(); // New line after progress
            Console.WriteLine($"\n[ERROR] Model download failed: {ex.Message}");
            Console.WriteLine();
            Console.WriteLine("Manual download instructions:");
            Console.WriteLine($"  1. Download from: {url}");
            Console.WriteLine($"  2. Save to: {modelPath}");
            Console.WriteLine();
            throw;
        }

        return modelPath;
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
