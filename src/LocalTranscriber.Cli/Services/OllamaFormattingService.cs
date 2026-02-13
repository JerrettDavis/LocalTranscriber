using System.Net.Http;
using LocalTranscriber.Cli.Models;
using OllamaSharp;

namespace LocalTranscriber.Cli.Services;

internal sealed class OllamaFormattingService
{
    public async Task<bool> IsHealthyAsync(Uri baseUri)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(2) };
            using var resp = await http.GetAsync("api/tags");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> FormatToMarkdownAsync(
        Uri baseUri,
        string model,
        Transcript transcript,
        FormatterTuningOptions? options = null)
    {
        var tuned = (options ?? new FormatterTuningOptions()).Normalized();
        var ollama = new OllamaApiClient(baseUri)
        {
            SelectedModel = model
        };

        var prompt = TranscriptionPromptFactory.BuildCleanupPrompt(transcript, tuned);

        var chunks = new List<string>();
        await foreach (var stream in ollama.GenerateAsync(prompt))
            chunks.Add(stream?.Response ?? string.Empty);

        var result = string.Concat(chunks).Trim();
        if (string.IsNullOrWhiteSpace(result))
            return new MarkdownFormatterService().FormatBasicMarkdown(transcript, tuned);

        if (!HasReasonableTranscriptOverlap(result, transcript.PlainText, tuned.EffectiveOverlapThreshold))
            return new MarkdownFormatterService().FormatBasicMarkdown(transcript, tuned);

        var normalized = tuned.StrictTranscript
            ? EnsureVerbatimTranscriptSection(result, transcript.PromptText)
            : result;

        return ApplyActionItemsPolicy(normalized, tuned.IncludeActionItems);
    }

    private static bool HasReasonableTranscriptOverlap(
        string candidateMarkdown,
        string sourceText,
        double threshold)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
            return true;

        var sourceTokens = Tokenize(sourceText).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (sourceTokens.Length < 8)
            return true;

        var candidateSet = Tokenize(candidateMarkdown)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matched = sourceTokens.Count(candidateSet.Contains);
        var ratio = matched / (double)sourceTokens.Length;
        return ratio >= threshold;
    }

    private static IEnumerable<string> Tokenize(string text)
        => text.Split(
            [' ', '\r', '\n', '\t', '.', ',', ':', ';', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}', '-', '_', '/'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 2);

    private static string EnsureVerbatimTranscriptSection(string markdown, string transcriptText)
    {
        var verbatim = transcriptText.Trim();
        if (string.IsNullOrWhiteSpace(verbatim))
            return markdown;

        var headingIndex = markdown.IndexOf("## Transcript", StringComparison.OrdinalIgnoreCase);
        if (headingIndex < 0)
            return $"{markdown.TrimEnd()}\n\n## Transcript\n\n{verbatim}\n";

        var endOfHeading = markdown.IndexOf('\n', headingIndex);
        if (endOfHeading < 0)
            return $"{markdown.TrimEnd()}\n\n## Transcript\n\n{verbatim}\n";

        var before = markdown[..(endOfHeading + 1)];
        var tail = markdown[(endOfHeading + 1)..];

        var nextHeader = tail.IndexOf("\n## ", StringComparison.Ordinal);
        if (nextHeader >= 0)
        {
            var suffix = tail[nextHeader..];
            return $"{before.TrimEnd()}\n\n{verbatim}\n{suffix}";
        }

        return $"{before.TrimEnd()}\n\n{verbatim}\n";
    }

    private static string ApplyActionItemsPolicy(string markdown, bool includeActionItems)
    {
        if (includeActionItems)
            return markdown;

        var headingIndex = markdown.IndexOf("## Action Items", StringComparison.OrdinalIgnoreCase);
        if (headingIndex < 0)
            return markdown;

        var endOfHeading = markdown.IndexOf('\n', headingIndex);
        if (endOfHeading < 0)
            return markdown;

        var before = markdown[..(endOfHeading + 1)];
        var tail = markdown[(endOfHeading + 1)..];
        var nextHeader = tail.IndexOf("\n## ", StringComparison.Ordinal);
        var suffix = nextHeader >= 0 ? tail[nextHeader..] : string.Empty;

        return $"{before.TrimEnd()}\n\n- []{suffix}";
    }
}
