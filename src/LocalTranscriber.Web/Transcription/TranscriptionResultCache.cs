using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LocalTranscriber.Web.Transcription;

internal sealed class TranscriptionResultCache
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _signatureLocks = new(StringComparer.Ordinal);
    private readonly string _cacheDir;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public TranscriptionResultCache()
    {
        _cacheDir = Path.Combine(Directory.GetCurrentDirectory(), "output", "cache");
    }

    public SemaphoreSlim GetSignatureLock(string signature)
        => _signatureLocks.GetOrAdd(signature, _ => new SemaphoreSlim(1, 1));

    public async Task<CachedTranscriptionResult?> TryGetAsync(string signature, CancellationToken ct = default)
    {
        var path = GetCachePath(signature);
        if (!File.Exists(path))
            return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<CachedTranscriptionResult>(stream, _json, ct);
    }

    public async Task SaveAsync(string signature, CachedTranscriptionResult result, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_cacheDir);
        var path = GetCachePath(signature);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, result, _json, ct);
    }

    public string ComputeRequestSignature(TranscriptionJobRequest request)
    {
        var sb = new StringBuilder(1024);
        sb.AppendLine($"audio:{Normalize(request.InputChecksum)}");
        sb.AppendLine($"model:{Normalize(request.Model)}");
        sb.AppendLine($"language:{Normalize(request.Language)}");
        sb.AppendLine($"maxSegmentLength:{request.MaxSegmentLength}");
        sb.AppendLine($"enableSpeakerLabels:{request.EnableSpeakerLabels}");
        sb.AppendLine($"speakerCount:{NullableInt(request.SpeakerCount)}");
        sb.AppendLine($"speakerSensitivity:{request.SpeakerSensitivity}");
        sb.AppendLine($"speakerMinScoreGain:{NullableDouble(request.SpeakerMinScoreGain)}");
        sb.AppendLine($"speakerMaxSwitchRate:{NullableDouble(request.SpeakerMaxSwitchRate)}");
        sb.AppendLine($"speakerMinSeparation:{NullableDouble(request.SpeakerMinSeparation)}");
        sb.AppendLine($"speakerMinClusterSize:{NullableInt(request.SpeakerMinClusterSize)}");
        sb.AppendLine($"speakerMaxAutoSpeakers:{NullableInt(request.SpeakerMaxAutoSpeakers)}");
        sb.AppendLine($"speakerGlobalVarianceGate:{NullableDouble(request.SpeakerGlobalVarianceGate)}");
        sb.AppendLine($"speakerShortRunMergeSeconds:{NullableDouble(request.SpeakerShortRunMergeSeconds)}");
        sb.AppendLine($"formatSensitivity:{request.FormatSensitivity}");
        sb.AppendLine($"formatStrictTranscript:{request.FormatStrictTranscript}");
        sb.AppendLine($"formatOverlapThreshold:{NullableDouble(request.FormatOverlapThreshold)}");
        sb.AppendLine($"formatSummaryMinBullets:{NullableInt(request.FormatSummaryMinBullets)}");
        sb.AppendLine($"formatSummaryMaxBullets:{NullableInt(request.FormatSummaryMaxBullets)}");
        sb.AppendLine($"formatIncludeActionItems:{request.FormatIncludeActionItems}");
        sb.AppendLine($"formatTemperature:{NullableDouble(request.FormatTemperature)}");
        sb.AppendLine($"formatMaxTokens:{NullableInt(request.FormatMaxTokens)}");
        sb.AppendLine($"formatLocalBigGapSeconds:{NullableDouble(request.FormatLocalBigGapSeconds)}");
        sb.AppendLine($"formatLocalSmallGapSeconds:{NullableDouble(request.FormatLocalSmallGapSeconds)}");
        sb.AppendLine($"formatProvider:{request.FormatProvider}");
        sb.AppendLine($"ollamaUri:{Normalize(request.OllamaUri)}");
        sb.AppendLine($"ollamaModel:{Normalize(request.OllamaModel)}");
        sb.AppendLine($"hfEndpoint:{Normalize(request.HuggingFaceEndpoint)}");
        sb.AppendLine($"hfModel:{Normalize(request.HuggingFaceModel)}");
        sb.AppendLine($"hfTokenPresent:{!string.IsNullOrWhiteSpace(request.HuggingFaceApiKey)}");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string GetCachePath(string signature)
        => Path.Combine(_cacheDir, $"{signature}.json");

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "null"
            : value.Trim().ToLowerInvariant();

    private static string NullableInt(int? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "null";

    private static string NullableDouble(double? value)
        => value?.ToString("G17", CultureInfo.InvariantCulture) ?? "null";
}
