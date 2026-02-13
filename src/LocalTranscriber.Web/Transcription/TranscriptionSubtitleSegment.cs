namespace LocalTranscriber.Web.Transcription;

public sealed record TranscriptionSubtitleWord(
    string Text,
    double StartSeconds,
    double EndSeconds);

public sealed record TranscriptionSubtitleSegment(
    double StartSeconds,
    double EndSeconds,
    string Text,
    string? Speaker = null,
    IReadOnlyList<TranscriptionSubtitleWord>? Words = null);
