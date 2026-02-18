using System.Runtime.InteropServices;

namespace LocalTranscriber.Tests.E2E.Reporting;

public enum StepStatus { Passed, Failed, Skipped }

public class ProfileScreenshot
{
    public required string ProfileName { get; init; }
    public required byte[] Data { get; init; }
}

public class StepResult
{
    public required string Keyword { get; init; }
    public required string Text { get; init; }
    public required StepStatus Status { get; init; }
    public string? Error { get; init; }
    public string? StackTrace { get; init; }
    public TimeSpan Duration { get; init; }
    public List<ProfileScreenshot> Screenshots { get; init; } = [];
}

public class ScenarioResult
{
    public required string Title { get; init; }
    public required string FeatureTitle { get; init; }
    public required string[] Tags { get; init; }
    public List<StepResult> Steps { get; } = [];
    public bool HasError => Steps.Any(s => s.Status == StepStatus.Failed);
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime CompletedAt { get; set; }
    public TimeSpan Duration => CompletedAt - StartedAt;
}

public class TestRunMetadata
{
    public DateTime StartedAt { get; init; }
    public DateTime CompletedAt { get; set; }
    public TimeSpan Duration => CompletedAt - StartedAt;
    public string Environment { get; init; } = RuntimeInformation.FrameworkDescription;
    public string MachineName { get; init; } = System.Environment.MachineName;
}
