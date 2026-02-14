namespace LocalTranscriber.Web.Plugins;

public sealed record StepInput(
    string Text,
    string? RawText,
    string? LabeledText,
    Dictionary<string, object>? Variables,
    Dictionary<string, object>? Structured);
