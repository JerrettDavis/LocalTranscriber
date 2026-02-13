using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LocalTranscriber.Cli.Services;

internal sealed class AudioNormalizeService
{
    private const int MinimumViableBytes = 128;
    private const double MinimumViableSeconds = 0.25;

    public async Task<string> Ensure16kMonoWavAsync(string inputPath)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Input file not found", inputPath);

        var fullIn = Path.GetFullPath(inputPath);
        var inputInfo = new FileInfo(fullIn);
        if (inputInfo.Length < MinimumViableBytes)
            throw new InvalidDataException(
                $"Input audio file is too small ({inputInfo.Length} bytes): {fullIn}. " +
                "Please provide a valid recording.");

        var ext = Path.GetExtension(fullIn).ToLowerInvariant();

        // If the file already looks like the desired format, don't touch it.
        if (ext == ".wav")
        {
            try
            {
                using var reader = new WaveFileReader(fullIn);
                EnsureViableWave(reader, fullIn);
                var wf = reader.WaveFormat;
                if (wf.Encoding == WaveFormatEncoding.Pcm && wf.SampleRate == 16000 && wf.Channels == 1)
                    return fullIn;
            }
            catch
            {
                // fallthrough to resample
            }
        }

        var outDir = Path.Combine(Path.GetDirectoryName(fullIn)!, "normalized");
        Directory.CreateDirectory(outDir);

        var outPath = Path.Combine(outDir, $"{Path.GetFileNameWithoutExtension(fullIn)}.16k.mono.wav");
        TryDelete(outPath);

        await Task.Run(() => ConvertTo16kMonoWav(fullIn, outPath));

        using (var reader = new WaveFileReader(outPath))
            EnsureViableWave(reader, outPath);

        return outPath;
    }

    private static void ConvertTo16kMonoWav(string inputPath, string outputWavPath)
    {
        // MediaFoundationReader can open common formats on Windows (wav/mp3/m4a/etc).
        // For maximum compatibility across OSes, swap this out for ffmpeg.
        using var reader = new MediaFoundationReader(inputPath);

        // Convert to mono float sample provider, then to 16k
        ISampleProvider sampleProvider = reader.ToSampleProvider();
        if (sampleProvider.WaveFormat.Channels > 1)
        {
            sampleProvider = new StereoToMonoSampleProvider(sampleProvider)
            {
                LeftVolume = 0.5f,
                RightVolume = 0.5f
            };
        }

        // Resample in float space and then encode as PCM16 for Whisper parser compatibility.
        var resampled = new WdlResamplingSampleProvider(sampleProvider, 16000);
        var pcm16 = new SampleToWaveProvider16(resampled);
        WaveFileWriter.CreateWaveFile(outputWavPath, pcm16);
    }

    private static void EnsureViableWave(WaveFileReader reader, string path)
    {
        if (reader.Length < MinimumViableBytes || reader.TotalTime.TotalSeconds < MinimumViableSeconds)
        {
            throw new InvalidDataException(
                $"Audio file appears invalid or too short for transcription ({reader.TotalTime.TotalSeconds:0.00}s): {path}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Ignore stale cache cleanup failures and continue with overwrite path.
        }
    }
}
