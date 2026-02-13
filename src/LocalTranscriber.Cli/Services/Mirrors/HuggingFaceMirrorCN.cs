namespace LocalTranscriber.Cli.Services.Mirrors;

/// <summary>
/// HuggingFace Mirror (hf-mirror.com) - community mirror often accessible when HF is blocked.
/// </summary>
internal sealed class HuggingFaceMirrorCN : IModelMirror
{
    private const string BaseUrl = "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main";
    
    public string Name => "HF-Mirror";
    public int Priority => 15;
    public bool IsEnabled => true;
    
    public string GetDownloadUrl(string modelFileName) => $"{BaseUrl}/{modelFileName}";
    
    public async Task<bool> ProbeAsync(string modelFileName, CancellationToken ct = default)
    {
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
