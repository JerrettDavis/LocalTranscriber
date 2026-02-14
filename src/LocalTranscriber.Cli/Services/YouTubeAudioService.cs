using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace LocalTranscriber.Cli.Services;

public sealed class YouTubeAudioService
{
    public record YouTubeAudioResult(
        string AudioFilePath,
        string VideoTitle,
        TimeSpan Duration,
        string VideoId);

    public async Task<YouTubeAudioResult> DownloadAudioAsync(
        string url,
        string outputDirectory,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var videoId = VideoId.TryParse(url)
            ?? throw new ArgumentException($"Invalid YouTube URL: {url}");

        var youtube = new YoutubeClient();

        var video = await youtube.Videos.GetAsync(videoId, ct);
        var title = video.Title;
        var duration = video.Duration ?? TimeSpan.Zero;

        var manifest = await youtube.Videos.Streams.GetManifestAsync(videoId, ct);

        var audioStream = manifest
            .GetAudioOnlyStreams()
            .OrderByDescending(s => s.Bitrate)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No audio streams available for this video.");

        var ext = audioStream.Container.Name; // e.g. "webm", "mp4"
        var outputPath = Path.Combine(outputDirectory, $"{videoId}.{ext}");

        await youtube.Videos.Streams.DownloadAsync(audioStream, outputPath, progress, ct);

        return new YouTubeAudioResult(outputPath, title, duration, videoId.Value);
    }

    public static bool IsYouTubeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        return VideoId.TryParse(url) is not null;
    }
}
