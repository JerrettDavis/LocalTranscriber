using System.Text.Json;
using LocalTranscriber.Web.Plugins;
using Microsoft.AspNetCore.SignalR;

namespace LocalTranscriber.Web.Transcription;

internal sealed class TranscriptionHub : Hub
{
    private readonly PluginLoader _pluginLoader;

    public TranscriptionHub(PluginLoader pluginLoader)
    {
        _pluginLoader = pluginLoader;
    }

    public Task JoinJob(string jobId)
        => Groups.AddToGroupAsync(Context.ConnectionId, jobId);

    public Task LeaveJob(string jobId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, jobId);

    public async Task<StepResult> ExecutePluginStep(string stepTypeId, string inputJson, string configJson)
    {
        var input = JsonSerializer.Deserialize<StepInput>(inputJson) ?? new StepInput("", null, null, null, null);
        var config = JsonSerializer.Deserialize<Dictionary<string, object>>(configJson) ?? new();
        return await _pluginLoader.ExecuteStepAsync(stepTypeId, input, config, Context.ConnectionAborted);
    }

    public async Task StreamAudioChunk(string jobId, string base64Audio)
    {
        // Stub: actual faster-whisper processing will be added
        // when the streaming service is implemented
        await Clients.Group(jobId).SendAsync("PartialTranscript", new
        {
            jobId,
            text = "",
            isFinal = false
        });
    }
}
