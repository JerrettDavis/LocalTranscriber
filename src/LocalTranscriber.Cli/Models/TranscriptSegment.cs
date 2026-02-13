namespace LocalTranscriber.Cli.Models;

/// <summary>
/// Represents a segment of transcribed speech with timing, text, and optional speaker/word data.
/// </summary>
public sealed record TranscriptSegment(
    TimeSpan Start,
    TimeSpan End,
    string Text,
    string? Speaker = null,
    TranscriptWordTiming[]? Words = null);
