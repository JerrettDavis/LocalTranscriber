using LocalTranscriber.Cli.Services;

namespace LocalTranscriber.Cli.Models;

/// <summary>
/// Customizable prompt templates for transcript formatting.
/// </summary>
internal record PromptTemplates
{
    /// <summary>
    /// System message for the LLM (defines the role/behavior).
    /// </summary>
    public string SystemMessage { get; init; } = DefaultSystemMessage;

    /// <summary>
    /// Main cleanup/formatting prompt template.
    /// Placeholders: {model}, {language}, {transcript}, {strictness}, {actionItemRule}, {summaryMinBullets}, {summaryMaxBullets}
    /// </summary>
    public string CleanupPrompt { get; init; } = DefaultCleanupPrompt;

    /// <summary>
    /// Output template that defines the expected Markdown structure.
    /// </summary>
    public string OutputTemplate { get; init; } = DefaultOutputTemplate;

    public static string DefaultSystemMessage => "You are a transcription editor.";

    public static string DefaultCleanupPrompt => """
Convert the following raw transcript into clean Markdown with structure and correct punctuation.

Rules:
- Do NOT add facts that are not present.
- Keep speaker intent the same.
- Fix obvious ASR errors when you can infer the intended word from context.
- Keep code-ish content as inline code or fenced blocks.
- {strictness}
- If uncertain, preserve the original wording instead of summarizing.
- Summary should contain between {summaryMinBullets} and {summaryMaxBullets} bullets.
- {actionItemRule}
""";

    public static string DefaultOutputTemplate => """
# Transcription

- **Model:** `{model}`
- **Language:** `{language}`

## Summary
- (3-8 bullet points)

## Action Items
- (use checkboxes like "- [ ]")

## Transcript
(Use paragraphs. Use headings only if the transcript strongly suggests it.)
""";

    /// <summary>
    /// Builds the complete prompt by combining templates and substituting placeholders.
    /// </summary>
    public string BuildPrompt(Transcript transcript, FormatterTuningOptions? options = null)
    {
        var tuned = (options ?? new FormatterTuningOptions()).Normalized();
        
        var strictness = tuned.StrictTranscript
            ? "Preserve transcript wording very strictly."
            : "You may lightly smooth wording while preserving meaning.";

        var actionItemRule = tuned.IncludeActionItems
            ? "Include actionable checkbox items if present."
            : "Keep Action Items minimal (use - [] when unclear).";

        var cleanupWithPlaceholders = CleanupPrompt
            .Replace("{strictness}", strictness)
            .Replace("{actionItemRule}", actionItemRule)
            .Replace("{summaryMinBullets}", tuned.EffectiveSummaryMinBullets.ToString())
            .Replace("{summaryMaxBullets}", tuned.EffectiveSummaryMaxBullets.ToString());

        var outputWithPlaceholders = OutputTemplate
            .Replace("{model}", transcript.Model)
            .Replace("{language}", transcript.Language);

        return $"""
{cleanupWithPlaceholders}

Output template (exact sections, in this order):
{outputWithPlaceholders}

Raw transcript:
{transcript.PromptText}
""";
    }
    
    /// <summary>
    /// Creates a copy with default values for any null/empty fields.
    /// </summary>
    public PromptTemplates WithDefaults() => this with
    {
        SystemMessage = string.IsNullOrWhiteSpace(SystemMessage) ? DefaultSystemMessage : SystemMessage,
        CleanupPrompt = string.IsNullOrWhiteSpace(CleanupPrompt) ? DefaultCleanupPrompt : CleanupPrompt,
        OutputTemplate = string.IsNullOrWhiteSpace(OutputTemplate) ? DefaultOutputTemplate : OutputTemplate
    };

    /// <summary>
    /// Resets all templates to defaults.
    /// </summary>
    public static PromptTemplates CreateDefault() => new();
}
