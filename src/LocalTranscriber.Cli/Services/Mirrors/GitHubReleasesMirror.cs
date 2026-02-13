namespace LocalTranscriber.Cli.Services.Mirrors;

/// <summary>
/// GitHub Releases mirror - community-hosted whisper GGML models.
/// Uses ddddwq2q/whisper-models which mirrors all standard GGML models.
/// </summary>
internal sealed class GitHubReleasesMirror : IModelMirror
{
    // Community mirror hosting all standard whisper GGML models
    private const string BaseUrl = "https://github.com/ddddwq2q/whisper-models/releases/download/Models";
    
    public string Name => "GitHub";
    public int Priority => 25;
    public bool IsEnabled => true;
    
    public string GetDownloadUrl(string modelFileName) => $"{BaseUrl}/{modelFileName}";
    
    public async Task<bool> ProbeAsync(string modelFileName, CancellationToken ct = default)
    {
        try
        {
            using var client = ResilientHttp.CreateClient(timeout: TimeSpan.FromSeconds(10));
            // GitHub redirects releases to CDN, check with HEAD
            using var request = new HttpRequestMessage(HttpMethod.Head, GetDownloadUrl(modelFileName));
            using var response = await client.SendAsync(request, ct);
            // GitHub returns 302 redirect to the actual asset
            return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Found;
        }
        catch
        {
            return false;
        }
    }
}
