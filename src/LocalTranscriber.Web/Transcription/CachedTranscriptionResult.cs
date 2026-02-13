namespace LocalTranscriber.Web.Transcription;

internal sealed record CachedTranscriptionResult(
    string InputChecksum,
    string RequestSignature,
    string CreatedAtUtc,
    string RawWhisperText,
    string SpeakerLabeledText,
    string FormatterOutput,
    string FormatterUsed,
    string Markdown,
    string OutputPath,
    int? DetectedSpeakerCount,
    IReadOnlyList<TranscriptionSubtitleSegment>? SubtitleSegments);
