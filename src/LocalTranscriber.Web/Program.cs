using LocalTranscriber.Cli.Services;
using LocalTranscriber.Web.Components;
using LocalTranscriber.Web.Plugins;
using LocalTranscriber.Web.Transcription;
using Microsoft.AspNetCore.SignalR;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSignalR(options =>
{
    // Recorded audio and large transcript payloads can exceed default SignalR limits.
    options.MaximumReceiveMessageSize = 20 * 1024 * 1024;
});
builder.Services.AddSingleton<TranscriptionJobProcessor>();
builder.Services.AddSingleton<TranscriptionResultCache>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<PluginLoader>();

builder.Services.Configure<HubOptions>(options =>
{
    options.MaximumReceiveMessageSize = 20 * 1024 * 1024;
});

var app = builder.Build();

// Load server-side workflow plugins
var pluginLoader = app.Services.GetRequiredService<PluginLoader>();
pluginLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "plugins"));

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapHub<TranscriptionHub>("/hubs/transcription");

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/transcriptions/{jobId}", async (string jobId, TranscriptionJobProcessor processor) =>
{
    var result = await processor.TryGetLastMessageAsync(jobId);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapPost("/api/transcriptions", async (
    HttpRequest request,
    TranscriptionJobProcessor processor,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("TranscriptionApi");
    var form = await request.ReadFormAsync(ct);
    var file = form.Files.GetFile("audio");
    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "Missing audio file in form field 'audio'." });

    var jobId = form["jobId"].ToString();
    if (string.IsNullOrWhiteSpace(jobId))
        jobId = Guid.NewGuid().ToString("N");

    var tempDir = Path.Combine(Path.GetTempPath(), "LocalTranscriber", "uploads");
    Directory.CreateDirectory(tempDir);

    var ext = Path.GetExtension(file.FileName);
    if (string.IsNullOrWhiteSpace(ext))
        ext = ".wav";

    var tempInput = Path.Combine(tempDir, $"{jobId}{ext}");
    await using (var stream = File.Create(tempInput))
    {
        await file.CopyToAsync(stream, ct);
    }
    var inputChecksum = await FileHashing.ComputeFileChecksumAsync(tempInput, ct);

    var req = BuildRequestFromForm(form, jobId, tempInput, inputChecksum);

    _ = Task.Run(async () =>
    {
        try
        {
            await processor.ProcessAsync(req);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled background failure for job {JobId}", req.JobId);
        }
    });

    return Results.Accepted($"/api/transcriptions/{jobId}", new { jobId });
}).DisableAntiforgery();

app.MapPost("/api/transcriptions/youtube", async (
    HttpRequest request,
    TranscriptionJobProcessor processor,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("TranscriptionApi");
    var form = await request.ReadFormAsync(ct);
    var youtubeUrl = form["youtubeUrl"].ToString();

    if (string.IsNullOrWhiteSpace(youtubeUrl))
        return Results.BadRequest(new { error = "Missing 'youtubeUrl' form field." });

    if (!YouTubeAudioService.IsYouTubeUrl(youtubeUrl))
        return Results.BadRequest(new { error = "Invalid YouTube URL." });

    var jobId = form["jobId"].ToString();
    if (string.IsNullOrWhiteSpace(jobId))
        jobId = Guid.NewGuid().ToString("N");

    var tempDir = Path.Combine(Path.GetTempPath(), "LocalTranscriber", "youtube");
    Directory.CreateDirectory(tempDir);

    var ytService = new YouTubeAudioService();
    YouTubeAudioService.YouTubeAudioResult ytResult;
    try
    {
        ytResult = await ytService.DownloadAudioAsync(youtubeUrl, tempDir, ct: ct);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"YouTube download failed: {ex.Message}" });
    }

    var inputChecksum = await FileHashing.ComputeFileChecksumAsync(ytResult.AudioFilePath, ct);

    var req = BuildRequestFromForm(form, jobId, ytResult.AudioFilePath, inputChecksum);

    _ = Task.Run(async () =>
    {
        try
        {
            await processor.ProcessAsync(req);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled background failure for job {JobId}", req.JobId);
        }
    });

    return Results.Accepted($"/api/transcriptions/{jobId}",
        new { jobId, videoTitle = ytResult.VideoTitle, duration = ytResult.Duration.TotalSeconds });
}).DisableAntiforgery();

app.MapPost("/api/youtube/download", async (
    HttpRequest request,
    CancellationToken ct) =>
{
    var form = await request.ReadFormAsync(ct);
    var youtubeUrl = form["youtubeUrl"].ToString();

    if (string.IsNullOrWhiteSpace(youtubeUrl))
        return Results.BadRequest(new { error = "Missing 'youtubeUrl' form field." });

    if (!YouTubeAudioService.IsYouTubeUrl(youtubeUrl))
        return Results.BadRequest(new { error = "Invalid YouTube URL." });

    var tempDir = Path.Combine(Path.GetTempPath(), "LocalTranscriber", "youtube-download");
    Directory.CreateDirectory(tempDir);

    var ytService = new YouTubeAudioService();
    YouTubeAudioService.YouTubeAudioResult ytResult;
    try
    {
        ytResult = await ytService.DownloadAudioAsync(youtubeUrl, tempDir, ct: ct);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"YouTube download failed: {ex.Message}" });
    }

    try
    {
        var bytes = await File.ReadAllBytesAsync(ytResult.AudioFilePath, ct);
        var base64 = Convert.ToBase64String(bytes);
        var ext = Path.GetExtension(ytResult.AudioFilePath).TrimStart('.');
        var mimeType = ext switch
        {
            "mp3" => "audio/mpeg",
            "m4a" => "audio/mp4",
            "ogg" => "audio/ogg",
            "wav" => "audio/wav",
            "webm" => "audio/webm",
            _ => "audio/mpeg"
        };
        var fileName = Path.GetFileName(ytResult.AudioFilePath);

        return Results.Ok(new
        {
            base64,
            mimeType,
            fileName,
            videoTitle = ytResult.VideoTitle,
            duration = ytResult.Duration.TotalSeconds
        });
    }
    finally
    {
        try { File.Delete(ytResult.AudioFilePath); } catch { }
    }
}).DisableAntiforgery();

app.MapPost("/api/workflow/transcribe", async (
    HttpRequest request,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("WorkflowTranscribeApi");
    var form = await request.ReadFormAsync(ct);
    var file = form.Files.GetFile("audio");
    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "Missing audio file in form field 'audio'." });

    var modelName = form["model"].ToString().NullIfEmpty() ?? "SmallEn";
    var language = form["language"].ToString().NullIfEmpty() ?? "auto";

    var tempDir = Path.Combine(Path.GetTempPath(), "LocalTranscriber", "workflow-transcribe");
    Directory.CreateDirectory(tempDir);

    var ext = Path.GetExtension(file.FileName);
    if (string.IsNullOrWhiteSpace(ext)) ext = ".wav";
    var tempInput = Path.Combine(tempDir, $"wf-{Guid.NewGuid():N}{ext}");

    try
    {
        await using (var stream = File.Create(tempInput))
        {
            await file.CopyToAsync(stream, ct);
        }

        var normalizeService = new LocalTranscriber.Cli.Services.AudioNormalizeService();
        var wavPath = await normalizeService.Ensure16kMonoWavAsync(tempInput);

        var whisperService = new LocalTranscriber.Cli.Services.WhisperTranscriptionService();
        var transcript = await whisperService.TranscribeAsync(wavPath, modelName, language, 0);

        var rawText = string.Join(" ", transcript.Segments
            .Where(s => !string.IsNullOrWhiteSpace(s.Text))
            .Select(s => s.Text.Trim()));

        var segments = BuildSubtitleSegmentsFromTranscript(transcript);

        return Results.Ok(new { rawText, segments });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Workflow transcribe failed");
        return Results.Problem($"Transcription failed: {ex.Message}");
    }
    finally
    {
        try { if (File.Exists(tempInput)) File.Delete(tempInput); } catch { }
    }
}).DisableAntiforgery();

app.MapPost("/api/workflow/speaker-labels", async (
    HttpRequest request,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("WorkflowSpeakerLabelsApi");
    var form = await request.ReadFormAsync(ct);
    var file = form.Files.GetFile("audio");
    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "Missing audio file in form field 'audio'." });

    var segmentsJson = form["segments"].ToString();
    if (string.IsNullOrWhiteSpace(segmentsJson))
        return Results.BadRequest(new { error = "Missing 'segments' form field." });

    var sensitivity = ParseInt(form["sensitivity"], 25);
    var speakerCount = ParseInt(form["speakerCount"], 0);

    var tempDir = Path.Combine(Path.GetTempPath(), "LocalTranscriber", "workflow-speakers");
    Directory.CreateDirectory(tempDir);

    var ext = Path.GetExtension(file.FileName);
    if (string.IsNullOrWhiteSpace(ext)) ext = ".wav";
    var tempInput = Path.Combine(tempDir, $"spk-{Guid.NewGuid():N}{ext}");

    try
    {
        await using (var stream = File.Create(tempInput))
        {
            await file.CopyToAsync(stream, ct);
        }

        var normalizeService = new LocalTranscriber.Cli.Services.AudioNormalizeService();
        var wavPath = await normalizeService.Ensure16kMonoWavAsync(tempInput);

        // Parse segments JSON into Transcript
        var rawSegments = System.Text.Json.JsonSerializer.Deserialize<WorkflowSegmentInput[]>(segmentsJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? Array.Empty<WorkflowSegmentInput>();

        var transcriptSegments = rawSegments.Select(s => new LocalTranscriber.Cli.Models.TranscriptSegment(
            Start: TimeSpan.FromSeconds(s.StartSeconds),
            End: TimeSpan.FromSeconds(s.EndSeconds),
            Text: s.Text ?? "",
            Speaker: null,
            Words: s.Words?.Select(w => new LocalTranscriber.Cli.Models.TranscriptWordTiming(
                Text: w.Text ?? "",
                Start: TimeSpan.FromSeconds(w.StartSeconds),
                End: TimeSpan.FromSeconds(w.EndSeconds)
            )).ToArray()
        )).ToList();

        var transcript = new LocalTranscriber.Cli.Models.Transcript("workflow", "en", transcriptSegments);

        var options = new LocalTranscriber.Cli.Services.SpeakerLabelingOptions(Sensitivity: sensitivity);
        var speakerService = new LocalTranscriber.Cli.Services.SpeakerLabelService();
        var labeled = speakerService.LabelSpeakers(transcript, wavPath, speakerCount, options);

        var segments = BuildSubtitleSegmentsFromTranscript(labeled);
        var detectedCount = labeled.Segments
            .Select(s => s.Speaker)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .Count();

        return Results.Ok(new { labeledText = labeled.PromptText, speakerCount = detectedCount, segments });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Workflow speaker labeling failed");
        return Results.Problem($"Speaker labeling failed: {ex.Message}");
    }
    finally
    {
        try { if (File.Exists(tempInput)) File.Delete(tempInput); } catch { }
    }
}).DisableAntiforgery();

app.MapPost("/api/workflow/llm", async (
    HttpRequest request,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("WorkflowLlmApi");

    WorkflowLlmRequest body;
    try
    {
        body = await request.ReadFromJsonAsync<WorkflowLlmRequest>(ct)
            ?? throw new InvalidOperationException("Empty request body");
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = $"Invalid JSON body: {ex.Message}" });
    }

    var systemPrompt = body.SystemPrompt ?? "";
    var userPrompt = body.UserPrompt ?? "";
    var temperature = body.Temperature ?? 0.3;
    var maxTokens = body.MaxTokens ?? 2000;
    var provider = body.Provider?.ToLowerInvariant();

    // ── OpenAI (explicit provider) ──
    if (provider == "openai")
    {
        if (string.IsNullOrWhiteSpace(body.OpenaiApiKey))
            return Results.BadRequest(new { error = "OpenAI API key is required. Configure it in Settings." });

        try
        {
            using var http = new HttpClient { BaseAddress = new Uri("https://api.openai.com") };
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", body.OpenaiApiKey);

            var payload = new
            {
                model = body.OpenaiModel ?? "gpt-4o-mini",
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt },
                },
                temperature,
                max_tokens = maxTokens,
            };

            var resp = await http.PostAsJsonAsync("/v1/chat/completions", payload, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
            var text = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            return Results.Ok(new { text });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OpenAI call failed");
            return Results.BadRequest(new { error = $"OpenAI call failed: {ex.Message}" });
        }
    }

    // ── Anthropic (explicit provider) ──
    if (provider == "anthropic")
    {
        if (string.IsNullOrWhiteSpace(body.AnthropicApiKey))
            return Results.BadRequest(new { error = "Anthropic API key is required. Configure it in Settings." });

        try
        {
            using var http = new HttpClient { BaseAddress = new Uri("https://api.anthropic.com") };
            http.DefaultRequestHeaders.Add("x-api-key", body.AnthropicApiKey);
            http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var payload = new
            {
                model = body.AnthropicModel ?? "claude-sonnet-4-5-20250929",
                max_tokens = maxTokens,
                system = systemPrompt,
                messages = new object[]
                {
                    new { role = "user", content = userPrompt },
                },
                temperature,
            };

            var resp = await http.PostAsJsonAsync("/v1/messages", payload, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
            var text = json.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
            return Results.Ok(new { text });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Anthropic call failed");
            return Results.BadRequest(new { error = $"Anthropic call failed: {ex.Message}" });
        }
    }

    // ── Explicit HuggingFace provider ──
    if (provider == "huggingface")
    {
        if (string.IsNullOrWhiteSpace(body.HfApiKey))
            return Results.BadRequest(new { error = "HuggingFace API key is required. Configure it in Settings." });

        try
        {
            var hfEndpoint = body.HfEndpoint ?? "https://router.huggingface.co";
            var hfModel = body.HfModel ?? "Qwen/Qwen2.5-14B-Instruct";

            using var http = new HttpClient { BaseAddress = new Uri(hfEndpoint) };
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", body.HfApiKey);

            var payload = new
            {
                model = hfModel,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt },
                },
                temperature,
                max_tokens = maxTokens,
            };

            var resp = await http.PostAsJsonAsync("/v1/chat/completions", payload, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
            var text = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            return Results.Ok(new { text });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "HuggingFace call failed");
            return Results.BadRequest(new { error = $"HuggingFace call failed: {ex.Message}" });
        }
    }

    // ── Explicit Ollama provider (chat API) ──
    if (provider == "ollama")
    {
        var ollamaUri = new Uri(body.OllamaUri ?? "http://localhost:11434");
        var ollamaModel = body.OllamaModel ?? "mistral-nemo:12b";

        var ollamaService = new LocalTranscriber.Cli.Services.OllamaFormattingService();
        if (!await ollamaService.IsHealthyAsync(ollamaUri))
            return Results.BadRequest(new { error = $"Ollama is not reachable at {ollamaUri}" });

        try
        {
            using var http = new HttpClient { BaseAddress = ollamaUri };
            var options = new Dictionary<string, object> { ["temperature"] = temperature };
            if (body.ContextWindow is > 0)
                options["num_ctx"] = body.ContextWindow.Value;

            var payload = new
            {
                model = ollamaModel,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt },
                },
                stream = false,
                options,
            };
            var resp = await http.PostAsJsonAsync("/api/chat", payload, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
            var text = json.GetProperty("message").GetProperty("content").GetString() ?? "";
            return Results.Ok(new { text });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ollama call failed");
            return Results.BadRequest(new { error = $"Ollama call failed: {ex.Message}" });
        }
    }

    // ── Legacy behavior (no explicit provider): try Ollama → HuggingFace fallback ──
    var legacyOllamaUri = new Uri(body.OllamaUri ?? "http://localhost:11434");
    var legacyOllamaModel = body.OllamaModel ?? "mistral-nemo:12b";

    var legacyOllamaService = new LocalTranscriber.Cli.Services.OllamaFormattingService();
    if (await legacyOllamaService.IsHealthyAsync(legacyOllamaUri))
    {
        try
        {
            using var http = new HttpClient { BaseAddress = legacyOllamaUri };
            var options = new Dictionary<string, object> { ["temperature"] = temperature };
            if (body.ContextWindow is > 0)
                options["num_ctx"] = body.ContextWindow.Value;

            var payload = new
            {
                model = legacyOllamaModel,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt },
                },
                stream = false,
                options,
            };
            var resp = await http.PostAsJsonAsync("/api/chat", payload, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
            var text = json.GetProperty("message").GetProperty("content").GetString() ?? "";
            return Results.Ok(new { text });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ollama call failed, checking HuggingFace fallback");
        }
    }

    // Try HuggingFace fallback
    var hfApiKey = body.HfApiKey;
    if (!string.IsNullOrWhiteSpace(hfApiKey))
    {
        try
        {
            var hfEndpoint = body.HfEndpoint ?? "https://router.huggingface.co";
            var hfModel = body.HfModel ?? "Qwen/Qwen2.5-14B-Instruct";

            using var http = new HttpClient { BaseAddress = new Uri(hfEndpoint) };
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", hfApiKey);

            var payload = new
            {
                model = hfModel,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt },
                },
                temperature,
                max_tokens = maxTokens,
            };

            var resp = await http.PostAsJsonAsync("/v1/chat/completions", payload, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
            var text = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            return Results.Ok(new { text });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "HuggingFace call failed");
        }
    }

    return Results.BadRequest(new { error = "No LLM backend available. Configure Ollama or provide a HuggingFace API key." });
}).DisableAntiforgery();

app.MapGet("/api/ollama/models", async (string? uri, CancellationToken ct) =>
{
    var ollamaUri = new Uri(uri ?? "http://localhost:11434");
    try
    {
        using var http = new HttpClient { BaseAddress = ollamaUri, Timeout = TimeSpan.FromSeconds(5) };
        var resp = await http.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/tags", ct);
        var models = resp.GetProperty("models").EnumerateArray()
            .Select(m => m.GetProperty("name").GetString())
            .Where(n => !string.IsNullOrEmpty(n))
            .ToArray();
        return Results.Ok(new { models });
    }
    catch
    {
        return Results.Ok(new { models = Array.Empty<string>() });
    }
}).DisableAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static TranscriptionJobRequest BuildRequestFromForm(
    IFormCollection form, string jobId, string audioPath, string checksum)
{
    var formatProvider = ParseFormatProvider(form["formatProvider"]);
    return new TranscriptionJobRequest(
        JobId: jobId,
        InputAudioPath: audioPath,
        InputChecksum: checksum,
        Model: form["model"].ToString().NullIfEmpty() ?? "SmallEn",
        Language: form["language"].ToString().NullIfEmpty() ?? "auto",
        MaxSegmentLength: ParseInt(form["maxSegLength"], 0),
        EnableSpeakerLabels: ParseBool(form["enableSpeakerLabels"], true),
        SpeakerCount: ParseNullableInt(form["speakerCount"]),
        SpeakerSensitivity: ParseInt(form["speakerSensitivity"], 25),
        SpeakerMinScoreGain: ParseNullableDouble(form["speakerMinScoreGain"]),
        SpeakerMaxSwitchRate: ParseNullableDouble(form["speakerMaxSwitchRate"]),
        SpeakerMinSeparation: ParseNullableDouble(form["speakerMinSeparation"]),
        SpeakerMinClusterSize: ParseNullableInt(form["speakerMinClusterSize"]),
        SpeakerMaxAutoSpeakers: ParseNullableInt(form["speakerMaxAutoSpeakers"]),
        SpeakerGlobalVarianceGate: ParseNullableDouble(form["speakerGlobalVarianceGate"]),
        SpeakerShortRunMergeSeconds: ParseNullableDouble(form["speakerShortRunMergeSeconds"]),
        FormatSensitivity: ParseInt(form["formatSensitivity"], 50),
        FormatStrictTranscript: ParseBool(form["formatStrictTranscript"], true),
        FormatOverlapThreshold: ParseNullableDouble(form["formatOverlapThreshold"]),
        FormatSummaryMinBullets: ParseNullableInt(form["formatSummaryMinBullets"]),
        FormatSummaryMaxBullets: ParseNullableInt(form["formatSummaryMaxBullets"]),
        FormatIncludeActionItems: ParseBool(form["formatIncludeActionItems"], true),
        FormatTemperature: ParseNullableDouble(form["formatTemperature"]),
        FormatMaxTokens: ParseNullableInt(form["formatMaxTokens"]),
        FormatLocalBigGapSeconds: ParseNullableDouble(form["formatLocalBigGapSeconds"]),
        FormatLocalSmallGapSeconds: ParseNullableDouble(form["formatLocalSmallGapSeconds"]),
        FormatProvider: formatProvider,
        OllamaUri: form["ollamaUri"].ToString().NullIfEmpty() ?? "http://localhost:11434",
        OllamaModel: form["ollamaModel"].ToString().NullIfEmpty() ?? "mistral-nemo:12b",
        HuggingFaceEndpoint: form["hfEndpoint"].ToString().NullIfEmpty() ?? "https://router.huggingface.co",
        HuggingFaceModel: form["hfModel"].ToString().NullIfEmpty() ?? "Qwen/Qwen2.5-14B-Instruct",
        HuggingFaceApiKey: form["hfApiKey"].ToString().NullIfEmpty() ?? Environment.GetEnvironmentVariable("HF_TOKEN"),
        ModelMirrorName: form["modelMirrorName"].ToString().NullIfEmpty(),
        ModelMirrorUrl: form["modelMirrorUrl"].ToString().NullIfEmpty());
}

static bool ParseBool(string? value, bool defaultValue)
    => bool.TryParse(value, out var parsed) ? parsed : defaultValue;

static int ParseInt(string? value, int defaultValue)
    => int.TryParse(value, out var parsed) ? parsed : defaultValue;

static int? ParseNullableInt(string? value)
    => int.TryParse(value, out var parsed) ? parsed : null;

static double? ParseNullableDouble(string? value)
    => double.TryParse(value, out var parsed) ? parsed : null;

static TranscriptionFormatProvider ParseFormatProvider(string? value)
{
    var normalized = value?.Trim().ToLowerInvariant();
    return normalized switch
    {
        "ollama" => TranscriptionFormatProvider.Ollama,
        "huggingface" or "hf" => TranscriptionFormatProvider.HuggingFace,
        "local" or "none" => TranscriptionFormatProvider.Local,
        _ => TranscriptionFormatProvider.Auto
    };
}

static object[] BuildSubtitleSegmentsFromTranscript(LocalTranscriber.Cli.Models.Transcript transcript)
{
    var ordered = transcript.Segments
        .Where(s => !string.IsNullOrWhiteSpace(s.Text))
        .ToArray();

    var segments = new List<object>(ordered.Length);
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
            .Select(w => new
            {
                text = w.Text.Trim(),
                startSeconds = Math.Max(start, w.Start.TotalSeconds),
                endSeconds = Math.Max(Math.Max(start, w.Start.TotalSeconds) + 0.02, w.End.TotalSeconds)
            })
            .Where(w => !string.IsNullOrWhiteSpace(w.text))
            .OrderBy(w => w.startSeconds)
            .ToArray();

        var maxWordEnd = subtitleWords is { Length: > 0 } ? subtitleWords[^1].endSeconds : 0;
        var rawEnd = Math.Max(start + 0.08, Math.Max(segment.End.TotalSeconds, maxWordEnd));
        var end = rawEnd;

        if (!double.IsInfinity(nextStart))
            end = Math.Min(end, Math.Max(start + 0.08, nextStart - 0.02));
        if (end <= start)
            end = start + 0.08;

        segments.Add(new
        {
            startSeconds = start,
            endSeconds = end,
            text,
            speaker = segment.Speaker,
            words = subtitleWords?.Select(w => new
            {
                w.text,
                startSeconds = Math.Max(start, w.startSeconds),
                endSeconds = Math.Min(end, w.endSeconds)
            }).ToArray()
        });
    }

    return segments.ToArray();
}

static class StringExtensions
{
    public static string? NullIfEmpty(this string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}

static class FileHashing
{
    public static async Task<string> ComputeFileChecksumAsync(string path, CancellationToken ct)
    {
        using var stream = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            hash.AppendData(buffer, 0, read);

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}

sealed record WorkflowLlmRequest(
    string SystemPrompt, string UserPrompt,
    double? Temperature = null, int? MaxTokens = null,
    string? Provider = null,
    string? OllamaUri = null, string? OllamaModel = null,
    string? HfEndpoint = null, string? HfModel = null, string? HfApiKey = null,
    string? OpenaiApiKey = null, string? OpenaiModel = null,
    string? AnthropicApiKey = null, string? AnthropicModel = null,
    int? ContextWindow = null);

sealed record WorkflowSegmentInput(
    double StartSeconds, double EndSeconds, string? Text,
    WorkflowWordInput[]? Words = null);

sealed record WorkflowWordInput(
    string? Text, double StartSeconds, double EndSeconds);

public partial class Program { }
