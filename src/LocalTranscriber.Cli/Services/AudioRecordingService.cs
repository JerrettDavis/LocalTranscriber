using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace LocalTranscriber.Cli.Services;

internal sealed record CaptureDeviceInfo(int Index, string FriendlyName, string DeviceId);

internal sealed class AudioRecordingService
{
    public List<CaptureDeviceInfo> ListCaptureDevices()
    {
        // Microphone/capture devices
        var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select((d, i) => new CaptureDeviceInfo(i, d.FriendlyName, d.ID))
            .ToList();
        return devices;
    }

    public async Task RecordToWavAsync(int captureDeviceIndex, string outputWavPath, bool loopback, CancellationToken ct)
    {
        // WASAPI gives you decent fidelity and is stable on modern Windows.
        using var enumerator = new MMDeviceEnumerator();
        var device = loopback
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            : enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ElementAtOrDefault(captureDeviceIndex)
                ?? throw new ArgumentException($"No capture device at index {captureDeviceIndex}");

        using WasapiCapture capture = loopback ? new WasapiLoopbackCapture(device) : new WasapiCapture(device);

        // Whisper wants 16k mono PCM. We'll record at the device's native rate
        // and resample later.
        using var writer = new WaveFileWriter(outputWavPath, capture.WaveFormat);

        capture.DataAvailable += (_, e) =>
        {
            if (ct.IsCancellationRequested) return;
            writer.Write(e.Buffer, 0, e.BytesRecorded);
            writer.Flush();
        };

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception != null)
                tcs.TrySetException(e.Exception);
            else
                tcs.TrySetResult();
        };

        capture.StartRecording();

        try
        {
            while (!ct.IsCancellationRequested)
                await Task.Delay(100, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected
        }
        finally
        {
            capture.StopRecording();
        }

        await tcs.Task.ConfigureAwait(false);
    }
}
