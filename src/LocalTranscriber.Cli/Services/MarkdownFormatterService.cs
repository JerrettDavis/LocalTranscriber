using System.Text;
using LocalTranscriber.Cli.Models;

namespace LocalTranscriber.Cli.Services;

internal sealed class MarkdownFormatterService
{
    public string FormatBasicMarkdown(Transcript transcript, FormatterTuningOptions? options = null)
    {
        var tuned = (options ?? new FormatterTuningOptions()).Normalized();
        var sb = new StringBuilder();
        sb.AppendLine("# Transcription");
        sb.AppendLine();
        sb.AppendLine($"- **Model:** `{transcript.Model}`");
        sb.AppendLine($"- **Language:** `{transcript.Language}`");
        sb.AppendLine($"- **Generated:** {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine();
        sb.AppendLine("## Summary");

        var summaryBullets = BuildSummaryBullets(
            transcript,
            tuned.EffectiveSummaryMinBullets,
            tuned.EffectiveSummaryMaxBullets);

        foreach (var bullet in summaryBullets)
            sb.AppendLine($"- {bullet}");

        sb.AppendLine();
        sb.AppendLine("## Action Items");
        if (tuned.IncludeActionItems)
        {
            foreach (var item in BuildActionItems(transcript))
                sb.AppendLine($"- [ ] {item}");
        }
        else
        {
            sb.AppendLine("- []");
        }

        sb.AppendLine();
        sb.AppendLine("## Transcript");
        sb.AppendLine();

        var paragraphs = transcript.Segments.Any(s => !string.IsNullOrWhiteSpace(s.Speaker))
            ? ToSpeakerParagraphs(
                transcript.Segments,
                tuned.EffectiveLocalBigGapSeconds,
                tuned.EffectiveLocalSmallGapSeconds)
            : ToParagraphs(
                transcript.Segments,
                tuned.EffectiveLocalBigGapSeconds,
                tuned.EffectiveLocalSmallGapSeconds);

        foreach (var paragraph in paragraphs)
        {
            sb.AppendLine(paragraph);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static IEnumerable<string> ToParagraphs(
        IReadOnlyList<TranscriptSegment> segments,
        double bigGapSeconds,
        double smallGapSeconds)
    {
        var current = new StringBuilder();
        TimeSpan? lastEnd = null;

        foreach (var s in segments)
        {
            var text = s.Text.Trim();
            if (text.Length == 0) continue;

            var gap = lastEnd is null ? 0 : (s.Start - lastEnd.Value).TotalSeconds;
            var lastEndedSentence = current.Length > 0 && EndsSentence(current.ToString());

            var shouldBreak = current.Length > 0 && (
                gap >= bigGapSeconds ||
                (lastEndedSentence && gap >= smallGapSeconds)
            );

            if (shouldBreak)
            {
                yield return current.ToString().Trim();
                current.Clear();
            }

            if (current.Length > 0)
                current.Append(' ');

            current.Append(text);
            lastEnd = s.End;
        }

        if (current.Length > 0)
            yield return current.ToString().Trim();
    }

    private static IEnumerable<string> ToSpeakerParagraphs(
        IReadOnlyList<TranscriptSegment> segments,
        double bigGapSeconds,
        double smallGapSeconds)
    {
        var current = new StringBuilder();
        TimeSpan? blockStart = null;
        TimeSpan? lastEnd = null;
        string? currentSpeaker = null;

        foreach (var s in segments)
        {
            var text = s.Text.Trim();
            if (text.Length == 0) continue;

            var speaker = string.IsNullOrWhiteSpace(s.Speaker) ? "Speaker ?" : s.Speaker!;
            var gap = lastEnd is null ? 0 : (s.Start - lastEnd.Value).TotalSeconds;
            var lastEndedSentence = current.Length > 0 && EndsSentence(current.ToString());

            var shouldBreak = current.Length > 0 && (
                !string.Equals(speaker, currentSpeaker, StringComparison.Ordinal) ||
                gap >= bigGapSeconds ||
                (lastEndedSentence && gap >= smallGapSeconds)
            );

            if (shouldBreak && currentSpeaker is not null && blockStart is not null && lastEnd is not null)
            {
                yield return FormatSpeakerParagraph(currentSpeaker, blockStart.Value, lastEnd.Value, current.ToString());
                current.Clear();
                blockStart = null;
            }

            if (current.Length == 0)
            {
                currentSpeaker = speaker;
                blockStart = s.Start;
            }
            else
            {
                current.Append(' ');
            }

            current.Append(text);
            lastEnd = s.End;
        }

        if (current.Length > 0 && currentSpeaker is not null && blockStart is not null && lastEnd is not null)
            yield return FormatSpeakerParagraph(currentSpeaker, blockStart.Value, lastEnd.Value, current.ToString());
    }

    private static string FormatSpeakerParagraph(string speaker, TimeSpan start, TimeSpan end, string text)
        => $"**{speaker} [{start:hh\\:mm\\:ss} - {end:hh\\:mm\\:ss}]:** {text.Trim()}";

    private static List<string> BuildSummaryBullets(Transcript transcript, int minBullets, int maxBullets)
    {
        var plain = transcript.PlainText.Trim();
        if (plain.Length == 0)
            return ["No speech content detected."];

        var sentences = SplitSentences(plain).ToList();
        if (sentences.Count == 0)
            sentences.Add(plain);

        var target = Math.Clamp(sentences.Count, minBullets, maxBullets);
        var bullets = new List<string>(target);

        for (var i = 0; i < target; i++)
        {
            var sentence = sentences[Math.Min(i, sentences.Count - 1)];
            bullets.Add(sentence);
        }

        return bullets;
    }

    private static IEnumerable<string> BuildActionItems(Transcript transcript)
    {
        var plain = transcript.PlainText;
        if (string.IsNullOrWhiteSpace(plain))
            return ["Review transcript for completeness."];

        var items = new List<string> { "Review transcript accuracy and speaker labels." };

        if (plain.Contains('?', StringComparison.Ordinal))
            items.Add("Resolve open questions referenced in the transcript.");

        if (plain.Contains("next", StringComparison.OrdinalIgnoreCase) ||
            plain.Contains("follow up", StringComparison.OrdinalIgnoreCase) ||
            plain.Contains("todo", StringComparison.OrdinalIgnoreCase))
            items.Add("Track follow-up tasks mentioned in the transcript.");

        return items.Take(3);
    }

    private static IEnumerable<string> SplitSentences(string text)
        => text.Split(['.', '!', '?'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Select(s => char.IsPunctuation(s[^1]) ? s : $"{s}.");

    private static bool EndsSentence(string text)
    {
        for (var i = text.Length - 1; i >= 0; i--)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) continue;
            return c is '.' or '!' or '?' or ':';
        }
        return false;
    }
}
