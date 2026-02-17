using Microsoft.Playwright;

namespace LocalTranscriber.Tests.E2E.Fixtures;

public class PlaywrightFixture : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public IBrowser Browser => _browser ?? throw new InvalidOperationException("Browser not initialized");

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();

        var headed = Environment.GetEnvironmentVariable("PLAYWRIGHT_HEADED") == "1";
        var slowMo = float.TryParse(
            Environment.GetEnvironmentVariable("PLAYWRIGHT_SLOW_MO"), out var sm) ? sm : 0;

        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = !headed,
            SlowMo = slowMo
        });
    }

    public async Task<(IBrowserContext Context, IPage Page)> NewContextAndPageAsync()
    {
        var context = await Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        return (context, page);
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
        _playwright?.Dispose();
    }
}
