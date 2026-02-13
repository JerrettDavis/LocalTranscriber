using System.Text;

namespace LocalTranscriber.Cli.Models;

/// <summary>
/// Represents a complete transcription result with metadata and segments.
/// </summary>
public sealed record Transcript(
    string Model,
    string Language,
    List<TranscriptSegment> Segments)
{
    /// <summary>
    /// Gets the plain text of all segments joined together.
    /// </summary>
    public string PlainText => BuildPlainText();

    /// <summary>
    /// Gets formatted text suitable for use in prompts (includes timestamps and speaker labels).
    /// </summary>
    public string PromptText => BuildPromptText();

    private string BuildPlainText()
    {
        if (Segments.Count == 0)
            return string.Empty;

        return string.Join(" ", Segments.Select(s => s.Text.Trim()));
    }

    private string BuildPromptText()
    {
        if (Segments.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var seg in Segments)
        {
            var timestamp = $"[{seg.Start:mm\\:ss} - {seg.End:mm\\:ss}]";
            var speaker = string.IsNullOrEmpty(seg.Speaker) ? "" : $" {seg.Speaker}:";
            sb.AppendLine($"{timestamp}{speaker} {seg.Text.Trim()}");
        }
        return sb.ToString().TrimEnd();
    }
}
