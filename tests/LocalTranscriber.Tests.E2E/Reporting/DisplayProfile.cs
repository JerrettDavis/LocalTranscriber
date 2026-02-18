namespace LocalTranscriber.Tests.E2E.Reporting;

public enum ColorScheme { Light, Dark }

public record DisplayProfile(string Name, int Width, int Height, ColorScheme ColorScheme);

public static class DisplayProfiles
{
    public static readonly IReadOnlyList<DisplayProfile> Default =
    [
        new("Desktop Light", 1920, 1080, ColorScheme.Light),
        new("Desktop Dark", 1920, 1080, ColorScheme.Dark),
        new("Tablet Light", 768, 1024, ColorScheme.Light),
        new("Tablet Dark", 768, 1024, ColorScheme.Dark),
        new("Mobile Light", 375, 812, ColorScheme.Light),
        new("Mobile Dark", 375, 812, ColorScheme.Dark),
    ];
}
