namespace LocalTranscriber.Cli.Services;

internal sealed record FormatterTuningOptions(
    int Sensitivity = 50,
    bool StrictTranscript = true,
    double? OverlapThreshold = null,
    int? SummaryMinBullets = null,
    int? SummaryMaxBullets = null,
    bool IncludeActionItems = true,
    double? Temperature = null,
    int? MaxTokens = null,
    double? LocalBigGapSeconds = null,
    double? LocalSmallGapSeconds = null)
{
    public FormatterTuningOptions Normalized()
    {
        var clampedSensitivity = Math.Clamp(Sensitivity, 0, 100);
        var summaryMin = Math.Clamp(SummaryMinBullets ?? 3, 1, 12);
        var summaryMax = Math.Clamp(SummaryMaxBullets ?? 8, summaryMin, 20);

        return this with
        {
            Sensitivity = clampedSensitivity,
            OverlapThreshold = ClampOptional(OverlapThreshold, 0.05, 0.95),
            SummaryMinBullets = summaryMin,
            SummaryMaxBullets = summaryMax,
            Temperature = ClampOptional(Temperature, 0.0, 1.2),
            MaxTokens = Math.Clamp(MaxTokens ?? 1200, 200, 6000),
            LocalBigGapSeconds = ClampOptional(LocalBigGapSeconds, 0.2, 10.0),
            LocalSmallGapSeconds = ClampOptional(LocalSmallGapSeconds, 0.1, 6.0)
        };
    }

    public double SensitivityFactor => Math.Clamp(Sensitivity, 0, 100) / 100.0;

    public double EffectiveOverlapThreshold
        => OverlapThreshold ?? Lerp(0.42, 0.20, SensitivityFactor);

    public int EffectiveSummaryMinBullets
        => Math.Clamp(SummaryMinBullets ?? 3, 1, 12);

    public int EffectiveSummaryMaxBullets
        => Math.Clamp(SummaryMaxBullets ?? 8, EffectiveSummaryMinBullets, 20);

    public double EffectiveTemperature
        => Temperature ?? Lerp(0.05, 0.35, SensitivityFactor);

    public int EffectiveMaxTokens
        => Math.Clamp(MaxTokens ?? 1200, 200, 6000);

    public double EffectiveLocalBigGapSeconds
        => LocalBigGapSeconds ?? Lerp(1.6, 0.9, SensitivityFactor);

    public double EffectiveLocalSmallGapSeconds
        => LocalSmallGapSeconds ?? Lerp(0.6, 0.3, SensitivityFactor);

    private static double? ClampOptional(double? value, double min, double max)
        => value is null ? null : Math.Clamp(value.Value, min, max);

    private static double Lerp(double low, double high, double t)
        => low + ((high - low) * Math.Clamp(t, 0.0, 1.0));
}
