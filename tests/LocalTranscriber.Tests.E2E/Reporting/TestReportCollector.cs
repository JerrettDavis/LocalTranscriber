using System.Collections.Concurrent;

namespace LocalTranscriber.Tests.E2E.Reporting;

public static class TestReportCollector
{
    private static readonly ConcurrentBag<ScenarioResult> _scenarios = [];
    private static readonly TestRunMetadata _metadata = new() { StartedAt = DateTime.UtcNow };

    public static void AddScenario(ScenarioResult result)
    {
        result.CompletedAt = DateTime.UtcNow;
        _scenarios.Add(result);
    }

    public static TestRunMetadata CompleteRun()
    {
        _metadata.CompletedAt = DateTime.UtcNow;
        return _metadata;
    }

    public static IReadOnlyList<string> GetAllTags() =>
        _scenarios
            .SelectMany(s => s.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<IGrouping<string, ScenarioResult>> GetResultsByFeature() =>
        _scenarios
            .GroupBy(s => s.FeatureTitle)
            .OrderBy(g => g.Key)
            .ToList();

    public static (int Total, int Passed, int Failed) GetSummary()
    {
        var all = _scenarios.ToList();
        var passed = all.Count(s => !s.HasError);
        var failed = all.Count(s => s.HasError);
        return (all.Count, passed, failed);
    }
}
