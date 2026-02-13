using LocalTranscriber.Cli.Models;

namespace LocalTranscriber.Cli.Services;

internal static class TranscriptionPromptFactory
{
    public static string BuildCleanupPrompt(Transcript transcript, FormatterTuningOptions? options = null)
    {
        var tuned = (options ?? new FormatterTuningOptions()).Normalized();
        var strictness = tuned.StrictTranscript
            ? "Preserve transcript wording very strictly."
            : "You may lightly smooth wording while preserving meaning.";

        var actionItemRule = tuned.IncludeActionItems
            ? "- Include actionable checkbox items if present."
            : "- Keep Action Items minimal (use - [] when unclear).";

        return $"""
You are a transcription editor.

Convert the following raw transcript into clean Markdown with structure and correct punctuation.

Rules:
- Do NOT add facts that are not present.
- Keep speaker intent the same.
- Fix obvious ASR errors when you can infer the intended word from context.
- Keep code-ish content as inline code or fenced blocks.
- {strictness}
- If uncertain, preserve the original wording instead of summarizing.
- Summary should contain between {tuned.EffectiveSummaryMinBullets} and {tuned.EffectiveSummaryMaxBullets} bullets.
- {actionItemRule}

Output template (exact sections, in this order):
# Transcription

- **Model:** `{transcript.Model}`
- **Language:** `{transcript.Language}`

## Summary
- (3-8 bullet points)

## Action Items
- (use checkboxes like "- [ ]")

## Transcript
(Use paragraphs. Use headings only if the transcript strongly suggests it.)

Raw transcript:
{transcript.PromptText}
""";
    }
}
