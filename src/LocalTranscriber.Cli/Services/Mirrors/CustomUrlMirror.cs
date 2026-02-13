namespace LocalTranscriber.Cli.Services.Mirrors;

/// <summary>
/// Custom URL mirror - for enterprise self-hosted models (Azure Blob, S3, internal servers).
/// Configure via --mirror-url flag or WHISPER_MODEL_MIRROR environment variable.
/// </summary>
internal sealed class CustomUrlMirror : IModelMirror
{
    private readonly string _baseUrl;
    
    public CustomUrlMirror(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
    }
    
    public string Name => "Custom";
    public int Priority => 1; // Highest priority when configured
    public bool IsEnabled => true;
    
    public string GetDownloadUrl(string modelFileName) => $"{_baseUrl}/{modelFileName}";
    
    public async Task<bool> ProbeAsync(string modelFileName, CancellationToken ct = default)
    {
        if (!IsEnabled) return false;
        
        try
        {
            using var client = ResilientHttp.CreateClient(timeout: TimeSpan.FromSeconds(10));
            using var request = new HttpRequestMessage(HttpMethod.Head, GetDownloadUrl(modelFileName));
            using var response = await client.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
