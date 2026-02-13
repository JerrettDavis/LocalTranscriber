namespace LocalTranscriber.Cli.Services.Mirrors;

/// <summary>
/// HuggingFace model mirror (primary source).
/// </summary>
internal sealed class HuggingFaceMirror : IModelMirror
{
    private const string BaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main";
    
    public string Name => "HuggingFace";
    public int Priority => 10;
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
