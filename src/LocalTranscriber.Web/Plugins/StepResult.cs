namespace LocalTranscriber.Web.Plugins;

public sealed record StepResult(
    string? ProcessedText,
    Dictionary<string, object>? Variables,
    Dictionary<string, object>? Structured,
    bool Success = true,
    string? ErrorMessage = null);
