using LocalTranscriber.Cli.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.HuggingFace;

namespace LocalTranscriber.Cli.Services;

internal sealed class HuggingFaceFormattingService
{
    public bool IsConfigured(string? apiKey)
        => !string.IsNullOrWhiteSpace(apiKey);

    public async Task<string> FormatToMarkdownAsync(
        Uri endpoint,
        string model,
        string apiKey,
        Transcript transcript,
        FormatterTuningOptions? options = null,
        CancellationToken ct = default)
    {
        var tuned = (options ?? new FormatterTuningOptions()).Normalized();
        var prompt = TranscriptionPromptFactory.BuildCleanupPrompt(transcript, tuned);
        var markdownFallback = new MarkdownFormatterService().FormatBasicMarkdown(transcript, tuned);

        Exception? lastError = null;

        foreach (var candidate in BuildEndpointCandidates(endpoint))
        {
            try
            {
                var kernelBuilder = Kernel.CreateBuilder();
                kernelBuilder.AddHuggingFaceChatCompletion(model, candidate, apiKey);

                var kernel = kernelBuilder.Build();
                var chat = kernel.Services.GetService<IChatCompletionService>()
                    ?? throw new InvalidOperationException("Hugging Face chat completion service is unavailable.");

                var history = new ChatHistory();
                history.AddSystemMessage("You are a transcription editor.");
                history.AddUserMessage(prompt);

                var settings = new HuggingFacePromptExecutionSettings
                {
                    Temperature = (float)tuned.EffectiveTemperature,
                    TopP = 0.9f,
                    MaxTokens = tuned.EffectiveMaxTokens,
                    WaitForModel = true
                };

                var response = await chat.GetChatMessageContentAsync(history, settings, kernel, ct);
                var result = response?.Content?.Trim();

                if (!string.IsNullOrWhiteSpace(result))
                {
                    if (!HasReasonableTranscriptOverlap(result, transcript.PlainText, tuned.EffectiveOverlapThreshold))
                        return markdownFallback;

                    var normalized = tuned.StrictTranscript
                        ? EnsureVerbatimTranscriptSection(result, transcript.PromptText)
                        : result;

                    return ApplyActionItemsPolicy(normalized, tuned.IncludeActionItems);
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (lastError is not null)
            throw new InvalidOperationException($"Hugging Face formatting failed: {lastError.Message}", lastError);

        return markdownFallback;
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

    private static IEnumerable<Uri> BuildEndpointCandidates(Uri endpoint)
    {
        yield return endpoint;

        var normalized = endpoint.ToString().TrimEnd('/');
        if (normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            yield return new Uri($"{normalized}/chat/completions");
            yield break;
        }

        if (normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            var baseV1 = normalized[..^"/chat/completions".Length];
            yield return new Uri(baseV1);
        }
    }
}
