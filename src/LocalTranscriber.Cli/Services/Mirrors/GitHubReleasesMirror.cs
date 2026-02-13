namespace LocalTranscriber.Cli.Services.Mirrors;

/// <summary>
/// GitHub Releases mirror - whisper.cpp official releases.
/// </summary>
internal sealed class GitHubReleasesMirror : IModelMirror
{
    // whisper.cpp releases include GGML models
    private const string BaseUrl = "https://github.com/ggerganov/whisper.cpp/releases/download/v1.7.3";
    
    public string Name => "GitHub";
    public int Priority => 25;
    public bool IsEnabled => true;
    
    public string GetDownloadUrl(string modelFileName) => $"{BaseUrl}/{modelFileName}";
    
    public async Task<bool> ProbeAsync(string modelFileName, CancellationToken ct = default)
    {
        try
        {
            using var client = ResilientHttp.CreateClient(timeout: TimeSpan.FromSeconds(10));
            // GitHub redirects, so we need to follow redirects for HEAD
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
