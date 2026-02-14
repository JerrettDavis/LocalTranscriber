using LocalTranscriber.Cli.Models;

namespace LocalTranscriber.Cli.Services;

internal static class TranscriptionPromptFactory
{
    /// <summary>
    /// Builds a cleanup prompt using default templates.
    /// </summary>
    public static string BuildCleanupPrompt(Transcript transcript, FormatterTuningOptions? options = null)
        => BuildCleanupPrompt(transcript, options, null);

    /// <summary>
    /// Builds a cleanup prompt using custom templates (or defaults if null).
    /// </summary>
    public static string BuildCleanupPrompt(
        Transcript transcript, 
        FormatterTuningOptions? options,
        PromptTemplates? templates)
    {
        var promptTemplates = (templates ?? new PromptTemplates()).WithDefaults();
        return promptTemplates.BuildPrompt(transcript, options);
    }

    /// <summary>
    /// Gets the system message from templates (or default).
    /// </summary>
    public static string GetSystemMessage(PromptTemplates? templates = null)
        => (templates ?? new PromptTemplates()).WithDefaults().SystemMessage;
}
