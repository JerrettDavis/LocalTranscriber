namespace LocalTranscriber.Cli.Models;

/// <summary>
/// Represents a single word with its timing information from transcription.
/// </summary>
public sealed record TranscriptWordTiming(
    string Text,
    TimeSpan Start,
    TimeSpan End);
