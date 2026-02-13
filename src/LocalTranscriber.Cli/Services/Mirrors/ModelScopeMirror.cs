namespace LocalTranscriber.Cli.Services.Mirrors;

/// <summary>
/// ModelScope (Alibaba) model mirror - common HuggingFace alternative.
/// </summary>
internal sealed class ModelScopeMirror : IModelMirror
{
    // ModelScope mirrors HuggingFace repos at this path pattern
    private const string BaseUrl = "https://modelscope.cn/models/ggerganov/whisper.cpp/resolve/main";
    
    public string Name => "ModelScope";
    public int Priority => 20;
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
