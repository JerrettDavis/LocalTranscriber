namespace LocalTranscriber.Cli.Services.Mirrors;

/// <summary>
/// Represents a model hosting mirror/provider.
/// </summary>
internal interface IModelMirror
{
    /// <summary>
    /// Display name of the mirror.
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Priority for auto-selection (lower = try first).
    /// </summary>
    int Priority { get; }
    
    /// <summary>
    /// Whether this mirror is enabled.
    /// </summary>
    bool IsEnabled { get; }
    
    /// <summary>
    /// Checks if the model is available on this mirror.
    /// </summary>
    Task<bool> ProbeAsync(string modelFileName, CancellationToken ct = default);
    
    /// <summary>
    /// Gets the download URL for a model file.
    /// </summary>
    string GetDownloadUrl(string modelFileName);
    
    /// <summary>
    /// Optional: Gets additional HTTP headers required for this mirror.
    /// </summary>
    IDictionary<string, string>? GetHeaders() => null;
}
