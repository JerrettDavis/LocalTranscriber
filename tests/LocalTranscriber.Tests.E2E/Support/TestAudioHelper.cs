namespace LocalTranscriber.Tests.E2E.Support;

public static class TestAudioHelper
{
    /// <summary>
    /// Generates a WAV file with silence (all zero samples).
    /// PCM 16-bit mono at the specified sample rate.
    /// </summary>
    public static byte[] GenerateSilenceWav(int durationMs = 1000, int sampleRate = 16000)
    {
        var numSamples = sampleRate * durationMs / 1000;
        var dataSize = numSamples * 2; // 16-bit = 2 bytes per sample
        var fileSize = 44 + dataSize; // 44-byte WAV header + data

        using var ms = new MemoryStream(fileSize);
        using var bw = new BinaryWriter(ms);

        // RIFF header
        bw.Write("RIFF"u8);
        bw.Write(fileSize - 8);
        bw.Write("WAVE"u8);

        // fmt sub-chunk
        bw.Write("fmt "u8);
        bw.Write(16);           // Sub-chunk size
        bw.Write((short)1);     // PCM format
        bw.Write((short)1);     // Mono
        bw.Write(sampleRate);   // Sample rate
        bw.Write(sampleRate * 2); // Byte rate (sampleRate * channels * bitsPerSample/8)
        bw.Write((short)2);     // Block align (channels * bitsPerSample/8)
        bw.Write((short)16);    // Bits per sample

        // data sub-chunk
        bw.Write("data"u8);
        bw.Write(dataSize);

        // Write silence (zeros)
        var silence = new byte[dataSize];
        bw.Write(silence);

        return ms.ToArray();
    }

    /// <summary>
    /// Creates a temporary WAV file with silence and returns the path.
    /// </summary>
    public static string CreateTempSilenceWav(int durationMs = 1000)
    {
        var wavBytes = GenerateSilenceWav(durationMs);
        var tempPath = Path.Combine(Path.GetTempPath(), $"test-silence-{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(tempPath, wavBytes);
        return tempPath;
    }
}
