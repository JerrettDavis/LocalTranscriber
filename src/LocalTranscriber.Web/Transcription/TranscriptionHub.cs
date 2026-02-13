using Microsoft.AspNetCore.SignalR;

namespace LocalTranscriber.Web.Transcription;

internal sealed class TranscriptionHub : Hub
{
    public Task JoinJob(string jobId)
        => Groups.AddToGroupAsync(Context.ConnectionId, jobId);

    public Task LeaveJob(string jobId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, jobId);
}
