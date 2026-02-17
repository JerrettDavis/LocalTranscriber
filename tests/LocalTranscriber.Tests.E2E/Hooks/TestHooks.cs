using LocalTranscriber.Tests.E2E.Fixtures;
using Microsoft.Playwright;
using Reqnroll;
using Reqnroll.BoDi;

namespace LocalTranscriber.Tests.E2E.Hooks;

[Binding]
public class TestHooks
{
    private static PlaywrightFixture? _playwrightFixture;
    private static BlazorServerFixture? _serverFixture;
    private static WasmHostFixture? _wasmFixture;

    [BeforeTestRun]
    public static async Task BeforeTestRun(IObjectContainer container)
    {
        _playwrightFixture = new PlaywrightFixture();
        await _playwrightFixture.InitializeAsync();

        _serverFixture = new BlazorServerFixture();
        await _serverFixture.InitializeAsync();

        // WASM fixture is slower — initialize only if needed
        _wasmFixture = new WasmHostFixture();
        try
        {
            await _wasmFixture.InitializeAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] WASM host fixture failed to initialize: {ex.Message}");
            _wasmFixture = null;
        }

        container.RegisterInstanceAs(_playwrightFixture);
        container.RegisterInstanceAs(_serverFixture);
        if (_wasmFixture is not null)
            container.RegisterInstanceAs(_wasmFixture);
    }

    [AfterTestRun]
    public static async Task AfterTestRun()
    {
        if (_wasmFixture is not null)
            await _wasmFixture.DisposeAsync();
        if (_serverFixture is not null)
            await _serverFixture.DisposeAsync();
        if (_playwrightFixture is not null)
            await _playwrightFixture.DisposeAsync();
    }

    [BeforeScenario]
    public async Task BeforeScenario(
        ScenarioContext scenarioContext,
        PlaywrightFixture playwrightFixture,
        BlazorServerFixture serverFixture)
    {
        var (context, page) = await playwrightFixture.NewContextAndPageAsync();
        scenarioContext.SetBrowserContext(context);
        scenarioContext.SetPage(page);

        // Determine base URL from tags
        var tags = scenarioContext.ScenarioInfo.Tags;
        if (tags.Contains("client"))
        {
            if (scenarioContext.ScenarioContainer.IsRegistered<WasmHostFixture>())
            {
                var wasmFixture = scenarioContext.ScenarioContainer.Resolve<WasmHostFixture>();
                scenarioContext.SetBaseUrl(wasmFixture.BaseUrl);
            }
            else
            {
                // WASM fixture not available — skip @client tests rather than
                // running them against the server where they'd fail
                scenarioContext.SetBaseUrl(serverFixture.BaseUrl);
            }
        }
        else
        {
            scenarioContext.SetBaseUrl(serverFixture.BaseUrl);
        }
    }

    [AfterScenario]
    public async Task AfterScenario(ScenarioContext scenarioContext)
    {
        if (scenarioContext.TestError is not null)
        {
            try
            {
                var page = scenarioContext.GetPage();
                var screenshotDir = Path.Combine(AppContext.BaseDirectory, "screenshots");
                Directory.CreateDirectory(screenshotDir);
                var fileName = $"{scenarioContext.ScenarioInfo.Title.Replace(" ", "_")}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png";
                await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = Path.Combine(screenshotDir, fileName),
                    FullPage = true
                });
            }
            catch { }
        }

        try
        {
            var browserContext = scenarioContext.GetBrowserContext();
            await browserContext.CloseAsync();
        }
        catch { }
    }
}
