namespace LocalTranscriber.Web.Transcription;

internal sealed record TranscriptionProgressMessage(
    string JobId,
    int Percent,
    string Stage,
    string Message,
    bool IsCompleted = false,
    bool IsError = false,
    string? RawWhisperText = null,
    string? SpeakerLabeledText = null,
    string? FormatterOutput = null,
    string? FormatterUsed = null,
    string? Markdown = null,
    string? OutputPath = null,
    int? DetectedSpeakerCount = null,
    IReadOnlyList<TranscriptionSubtitleSegment>? SubtitleSegments = null
);
