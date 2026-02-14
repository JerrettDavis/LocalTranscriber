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
