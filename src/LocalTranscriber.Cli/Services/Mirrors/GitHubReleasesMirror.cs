namespace LocalTranscriber.Cli.Services.Mirrors;

/// <summary>
/// GitHub Releases mirror - community-hosted whisper GGML models.
/// Uses ddddwq2q/whisper-models which mirrors all standard GGML models.
/// </summary>
/// <remarks>
/// Priority is dynamic: when running in GitHub Actions, this mirror is
/// preferred (priority 5) since GitHub CDN is more reliable than external
/// sources in CI environments.
/// </remarks>
internal sealed class GitHubReleasesMirror : IModelMirror
{
    // Community mirror hosting all standard whisper GGML models
    private const string BaseUrl = "https://github.com/ddddwq2q/whisper-models/releases/download/Models";
    
    // Detect GitHub Actions for priority boost
    private static readonly bool IsGitHubActions = 
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
    
    public string Name => "GitHub";
    
    // In GitHub Actions, use priority 5 (before HuggingFace's 10); otherwise 25 (last resort)
    public int Priority => IsGitHubActions ? 5 : 25;
    
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
