using System.Collections.Concurrent;
using LocalTranscriber.Cli.Services;
using Microsoft.AspNetCore.SignalR;

namespace LocalTranscriber.Web.Transcription;

internal sealed class TranscriptionJobProcessor
{
    private readonly IHubContext<TranscriptionHub> _hubContext;
    private readonly ILogger<TranscriptionJobProcessor> _logger;
    private readonly TranscriptionResultCache _resultCache;

    private readonly AudioNormalizeService _normalizer = new();
    private readonly WhisperTranscriptionService _whisper = new();
    private readonly SpeakerLabelService _speakerLabel = new();
    private readonly MarkdownFormatterService _markdownFormatter = new();
    private readonly OllamaFormattingService _ollama = new();
    private readonly HuggingFaceFormattingService _huggingFace = new();

    private readonly ConcurrentDictionary<string, TranscriptionProgressMessage> _latest = new();

    public TranscriptionJobProcessor(
        IHubContext<TranscriptionHub> hubContext,
        ILogger<TranscriptionJobProcessor> logger,
        TranscriptionResultCache resultCache)
    {
        _hubContext = hubContext;
        _logger = logger;
        _resultCache = resultCache;
    }

    public Task<TranscriptionProgressMessage?> TryGetLastMessageAsync(string jobId)
    {
        _latest.TryGetValue(jobId, out var msg);
        return Task.FromResult(msg);
    }

    public async Task ProcessAsync(TranscriptionJobRequest request, CancellationToken ct = default)
    {
        try
        {
            var requestSignature = _resultCache.ComputeRequestSignature(request);
            var signatureLock = _resultCache.GetSignatureLock(requestSignature);

            await PublishAsync(request.JobId, 5, "queued", "Job queued");

            await signatureLock.WaitAsync(ct);
            try
            {
                var cached = await _resultCache.TryGetAsync(requestSignature, ct);
                if (cached is not null)
                {
                    var cachedOutputPath = await EnsureCachedOutputPathAsync(cached, request.JobId, ct);
                    await PublishAsync(
                        request.JobId,
                        18,
                        "cache",
                        "Cache hit. Reusing previously computed transcription.",
                        rawWhisperText: cached.RawWhisperText,
                        speakerLabeledText: cached.SpeakerLabeledText,
                        formatterOutput: cached.FormatterOutput,
                        formatterUsed: cached.FormatterUsed,
                        markdown: cached.Markdown,
                        outputPath: cachedOutputPath,
                        detectedSpeakerCount: cached.DetectedSpeakerCount,
                        subtitleSegments: cached.SubtitleSegments);

                    await PublishAsync(
                        request.JobId,
                        100,
                        "done",
                        "Transcription complete (cache hit).",
                        isCompleted: true,
                        rawWhisperText: cached.RawWhisperText,
                        speakerLabeledText: cached.SpeakerLabeledText,
                        formatterOutput: cached.FormatterOutput,
                        formatterUsed: cached.FormatterUsed,
                        markdown: cached.Markdown,
                        outputPath: cachedOutputPath,
                        detectedSpeakerCount: cached.DetectedSpeakerCount,
                        subtitleSegments: cached.SubtitleSegments);
                    return;
                }

            await PublishAsync(request.JobId, 15, "normalize", "Normalizing audio...");
            var normalized = await _normalizer.Ensure16kMonoWavAsync(request.InputAudioPath);

            await PublishAsync(request.JobId, 40, "transcribe", $"Running Whisper model `{request.Model}`...");
            var downloadOptions = new ResilientModelDownloader.DownloadOptions(
                MirrorName: request.ModelMirrorName,
                MirrorUrl: request.ModelMirrorUrl,
                TrustAllCerts: false);
            var transcript = await _whisper.TranscribeAsync(
                normalized,
                request.Model,
                request.Language,
                request.MaxSegmentLength,
                downloadOptions);

            var rawWhisperText = transcript.PlainText;
            var subtitleSegments = BuildSubtitleSegments(transcript);
            await PublishAsync(
                request.JobId,
                52,
                "transcribe",
                "Whisper transcription complete.",
                rawWhisperText: rawWhisperText,
                subtitleSegments: subtitleSegments);

            int? detectedSpeakerCount = null;
            var speakerLabeledText = rawWhisperText;
            if (request.EnableSpeakerLabels)
            {
                await PublishAsync(request.JobId, 62, "speakers", "Detecting speaker changes...");
                var speakerOptions = new SpeakerLabelingOptions(
                    request.SpeakerSensitivity,
                    request.SpeakerMinScoreGain,
                    request.SpeakerMaxSwitchRate,
                    request.SpeakerMinSeparation,
                    request.SpeakerMinClusterSize,
                    request.SpeakerMaxAutoSpeakers,
                    request.SpeakerGlobalVarianceGate,
                    request.SpeakerShortRunMergeSeconds);

                transcript = _speakerLabel.LabelSpeakers(
                    transcript,
                    normalized,
                    request.SpeakerCount ?? 0,
                    speakerOptions);

                subtitleSegments = BuildSubtitleSegments(transcript);
                speakerLabeledText = transcript.PromptText;
                await PublishAsync(
                    request.JobId,
                    70,
                    "speakers",
                    "Speaker labeling complete.",
                    rawWhisperText: rawWhisperText,
                    speakerLabeledText: speakerLabeledText,
                    subtitleSegments: subtitleSegments);

                detectedSpeakerCount = transcript.Segments
                    .Select(s => s.Speaker)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
            }

            var formatterOptions = new FormatterTuningOptions(
                request.FormatSensitivity,
                request.FormatStrictTranscript,
                request.FormatOverlapThreshold,
                request.FormatSummaryMinBullets,
                request.FormatSummaryMaxBullets,
                request.FormatIncludeActionItems,
                request.FormatTemperature,
                request.FormatMaxTokens,
                request.FormatLocalBigGapSeconds,
                request.FormatLocalSmallGapSeconds);

            await PublishAsync(request.JobId, 80, "format", "Formatting transcript...");
            var formatResult = await FormatMarkdownAsync(request, transcript, formatterOptions, ct);

            var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output");
            Directory.CreateDirectory(outputDir);

            var outputPath = Path.Combine(
                outputDir,
                $"web-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{request.JobId[..8]}.md");
            await File.WriteAllTextAsync(outputPath, formatResult.FinalMarkdown, ct);

                var cacheEntry = new CachedTranscriptionResult(
                    request.InputChecksum,
                    requestSignature,
                    DateTimeOffset.UtcNow.ToString("O"),
                    rawWhisperText,
                    speakerLabeledText,
                    formatResult.FormatterOutput,
                    formatResult.FormatterUsed,
                    formatResult.FinalMarkdown,
                    outputPath,
                    detectedSpeakerCount,
                    subtitleSegments);
                try
                {
                    await _resultCache.SaveAsync(requestSignature, cacheEntry, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist cache for request signature {Signature}", requestSignature);
                }

            await PublishAsync(
                request.JobId,
                100,
                "done",
                "Transcription complete.",
                isCompleted: true,
                rawWhisperText: rawWhisperText,
                speakerLabeledText: speakerLabeledText,
                formatterOutput: formatResult.FormatterOutput,
                formatterUsed: formatResult.FormatterUsed,
                markdown: formatResult.FinalMarkdown,
                outputPath: outputPath,
                detectedSpeakerCount: detectedSpeakerCount,
                subtitleSegments: subtitleSegments);
            }
            finally
            {
                signatureLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transcription job {JobId} failed", request.JobId);
            await PublishAsync(
                request.JobId,
                100,
                "error",
                $"Transcription failed: {ex.Message}",
                isError: true);
        }
        finally
        {
            TryDeleteFile(request.InputAudioPath);
        }
    }

    private async Task<FormatResult> FormatMarkdownAsync(
        TranscriptionJobRequest request,
        LocalTranscriber.Cli.Models.Transcript transcript,
        FormatterTuningOptions formatterOptions,
        CancellationToken ct)
    {
        if (request.FormatProvider is TranscriptionFormatProvider.Auto or TranscriptionFormatProvider.Ollama)
        {
            if (await _ollama.IsHealthyAsync(new Uri(request.OllamaUri)))
            {
                await PublishAsync(request.JobId, 84, "format", $"Formatting with Ollama ({request.OllamaModel})...");
                var formatted = await _ollama.FormatToMarkdownAsync(
                    new Uri(request.OllamaUri),
                    request.OllamaModel,
                    transcript,
                    formatterOptions);
                await PublishAsync(
                    request.JobId,
                    89,
                    "format",
                    "Formatter output received from Ollama.",
                    formatterOutput: formatted,
                    formatterUsed: "ollama");
                return new FormatResult(formatted, formatted, "ollama");
            }
        }

        if (request.FormatProvider == TranscriptionFormatProvider.Ollama)
        {
            await PublishAsync(request.JobId, 86, "format", "Ollama unavailable, falling back to local formatter.");
            var fallback = _markdownFormatter.FormatBasicMarkdown(transcript, formatterOptions);
            return new FormatResult(fallback, fallback, "local");
        }

        if (request.FormatProvider is TranscriptionFormatProvider.Auto or TranscriptionFormatProvider.HuggingFace)
        {
            if (_huggingFace.IsConfigured(request.HuggingFaceApiKey))
            {
                try
                {
                    await PublishAsync(request.JobId, 88, "format", $"Formatting with Hugging Face ({request.HuggingFaceModel})...");
                    var formatted = await _huggingFace.FormatToMarkdownAsync(
                        new Uri(request.HuggingFaceEndpoint),
                        request.HuggingFaceModel,
                        request.HuggingFaceApiKey!,
                        transcript,
                        formatterOptions,
                        ct);
                    await PublishAsync(
                        request.JobId,
                        89,
                        "format",
                        "Formatter output received from Hugging Face.",
                        formatterOutput: formatted,
                        formatterUsed: "huggingface");
                    return new FormatResult(formatted, formatted, "huggingface");
                }
                catch (Exception ex)
                {
                    await PublishAsync(request.JobId, 90, "format", $"Hugging Face unavailable ({ex.Message}).");
                }
            }
            else if (request.FormatProvider == TranscriptionFormatProvider.HuggingFace)
            {
                await PublishAsync(request.JobId, 90, "format", "No HF token configured. Falling back to local formatter.");
            }
        }

        await PublishAsync(request.JobId, 92, "format", "Using local formatter.");
        var local = _markdownFormatter.FormatBasicMarkdown(transcript, formatterOptions);
        return new FormatResult(local, local, "local");
    }

    private async Task PublishAsync(
        string jobId,
        int percent,
        string stage,
        string message,
        bool isCompleted = false,
        bool isError = false,
        string? rawWhisperText = null,
        string? speakerLabeledText = null,
        string? formatterOutput = null,
        string? formatterUsed = null,
        string? markdown = null,
        string? outputPath = null,
        int? detectedSpeakerCount = null,
        IReadOnlyList<TranscriptionSubtitleSegment>? subtitleSegments = null)
    {
        var payload = new TranscriptionProgressMessage(
            jobId,
            percent,
            stage,
            message,
            isCompleted,
            isError,
            rawWhisperText,
            speakerLabeledText,
            formatterOutput,
            formatterUsed,
            markdown,
            outputPath,
            detectedSpeakerCount,
            subtitleSegments);

        _latest[jobId] = payload;
        await _hubContext.Clients.Group(jobId).SendAsync("JobProgress", payload);
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp file {Path}", path);
        }
    }

    private static async Task<string> EnsureCachedOutputPathAsync(
        CachedTranscriptionResult cached,
        string jobId,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(cached.OutputPath) && File.Exists(cached.OutputPath))
            return cached.OutputPath;

        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output");
        Directory.CreateDirectory(outputDir);

        var suffix = jobId.Length >= 8 ? jobId[..8] : jobId;
        var outputPath = Path.Combine(
            outputDir,
            $"web-cache-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{suffix}.md");
        await File.WriteAllTextAsync(outputPath, cached.Markdown, ct);
        return outputPath;
    }

    private static IReadOnlyList<TranscriptionSubtitleSegment> BuildSubtitleSegments(LocalTranscriber.Cli.Models.Transcript transcript)
    {
        var ordered = transcript.Segments
            .Where(s => !string.IsNullOrWhiteSpace(s.Text))
            .ToArray();

        var segments = new List<TranscriptionSubtitleSegment>(ordered.Length);
        for (var i = 0; i < ordered.Length; i++)
        {
            var segment = ordered[i];
            var text = segment.Text.Trim();
            var start = Math.Max(0, segment.Start.TotalSeconds);
            var nextStart = i < ordered.Length - 1
                ? Math.Max(start, ordered[i + 1].Start.TotalSeconds)
                : double.PositiveInfinity;

            var subtitleWords = segment.Words?
                .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                .Select(w =>
                {
                    var wordStart = Math.Max(start, w.Start.TotalSeconds);
                    var wordEnd = Math.Max(wordStart + 0.02, w.End.TotalSeconds);
                    return new TranscriptionSubtitleWord(
                        w.Text.Trim(),
                        wordStart,
                        wordEnd);
                })
                .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                .OrderBy(w => w.StartSeconds)
                .ToArray();

            var maxWordEnd = subtitleWords is { Length: > 0 }
                ? subtitleWords[^1].EndSeconds
                : 0;
            var rawEnd = Math.Max(start + 0.08, Math.Max(segment.End.TotalSeconds, maxWordEnd));
            var estimatedEnd = subtitleWords is { Length: > 0 }
                ? rawEnd
                : start + EstimateSubtitleDurationSeconds(text);
            var end = Math.Max(rawEnd, estimatedEnd);

            if (!double.IsInfinity(nextStart))
                end = Math.Min(end, Math.Max(start + 0.08, nextStart - 0.02));

            if (end <= start)
                end = start + 0.08;

            if (subtitleWords is { Length: > 0 })
            {
                for (var w = 0; w < subtitleWords.Length; w++)
                {
                    var word = subtitleWords[w];
                    var normalizedStart = Math.Max(start, word.StartSeconds);
                    var normalizedEnd = Math.Max(normalizedStart + 0.02, word.EndSeconds);
                    if (w == subtitleWords.Length - 1)
                        normalizedEnd = Math.Min(end, normalizedEnd);

                    subtitleWords[w] = word with
                    {
                        StartSeconds = normalizedStart,
                        EndSeconds = Math.Min(end, normalizedEnd)
                    };
                }
            }

            segments.Add(new TranscriptionSubtitleSegment(
                start,
                end,
                text,
                segment.Speaker,
                subtitleWords));
        }

        return segments;
    }

    private static double EstimateSubtitleDurationSeconds(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0.8;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        var byWords = words / 2.8;
        var byChars = text.Length * 0.052;
        return Math.Max(0.8, Math.Max(byWords, byChars));
    }

    private sealed record FormatResult(
        string FinalMarkdown,
        string FormatterOutput,
        string FormatterUsed);
}
