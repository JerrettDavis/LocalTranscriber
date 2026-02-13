namespace LocalTranscriber.Cli.Services;

internal sealed record SpeakerLabelingOptions(
    int Sensitivity = 25,
    double? MinScoreGainForSplit = null,
    double? MaxSwitchRateForSplit = null,
    double? MinClusterSeparation = null,
    int? MinClusterSize = null,
    int? MaxAutoSpeakers = null,
    double? GlobalVarianceGate = null,
    double? ShortRunMergeSeconds = null)
{
    private const int MinSensitivity = 0;
    private const int MaxSensitivity = 100;

    public SpeakerLabelingOptions Normalized()
        => this with
        {
            Sensitivity = Math.Clamp(Sensitivity, MinSensitivity, MaxSensitivity),
            MinScoreGainForSplit = ClampOptional(MinScoreGainForSplit, 0.0, 1.0),
            MaxSwitchRateForSplit = ClampOptional(MaxSwitchRateForSplit, 0.0, 1.0),
            MinClusterSeparation = ClampOptional(MinClusterSeparation, 0.1, 5.0),
            MinClusterSize = MinClusterSize is null ? null : Math.Clamp(MinClusterSize.Value, 1, 12),
            MaxAutoSpeakers = MaxAutoSpeakers is null ? null : Math.Clamp(MaxAutoSpeakers.Value, 1, 12),
            GlobalVarianceGate = ClampOptional(GlobalVarianceGate, 0.05, 4.0),
            ShortRunMergeSeconds = ClampOptional(ShortRunMergeSeconds, 0.2, 8.0)
        };

    public double SensitivityFactor => Math.Clamp(Sensitivity, MinSensitivity, MaxSensitivity) / 100.0;

    public double EffectiveMinScoreGainForSplit
        => MinScoreGainForSplit ?? Lerp(0.19, 0.05, SensitivityFactor);

    public double EffectiveMaxSwitchRateForSplit
        => MaxSwitchRateForSplit ?? Lerp(0.34, 0.58, SensitivityFactor);

    public double EffectiveMinClusterSeparation
        => MinClusterSeparation ?? Lerp(0.95, 0.55, SensitivityFactor);

    public int EffectiveMinClusterSize
        => MinClusterSize ?? (SensitivityFactor < 0.5 ? 2 : 1);

    public int EffectiveMaxAutoSpeakers
        => MaxAutoSpeakers ?? (SensitivityFactor < 0.35 ? 4 : 6);

    public double EffectiveGlobalVarianceGate
        => GlobalVarianceGate ?? Lerp(0.66, 0.42, SensitivityFactor);

    public double EffectiveShortRunMergeSeconds
        => ShortRunMergeSeconds ?? Lerp(1.9, 1.0, SensitivityFactor);

    public double EffectiveComplexityPenaltyPerSpeaker
        => Lerp(0.12, 0.05, SensitivityFactor);

    public double EffectiveSingletonClusterPenalty
        => Lerp(0.26, 0.12, SensitivityFactor);

    public double EffectiveImbalancePenaltyFactor
        => Lerp(0.08, 0.04, SensitivityFactor);

    public double EffectiveSwitchPenaltyShort
        => Lerp(0.30, 0.15, SensitivityFactor);

    public double EffectiveSwitchPenaltyLong
        => Lerp(0.18, 0.10, SensitivityFactor);

    private static double? ClampOptional(double? value, double min, double max)
        => value is null ? null : Math.Clamp(value.Value, min, max);

    private static double Lerp(double low, double high, double t)
        => low + ((high - low) * Math.Clamp(t, 0.0, 1.0));
}
