using LocalTranscriber.Tests.E2E.Fixtures;
using LocalTranscriber.Tests.E2E.Reporting;
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
        var metadata = TestReportCollector.CompleteRun();

        var reportPath = Path.Combine(AppContext.BaseDirectory, "reports",
            $"e2e-report_{DateTime.UtcNow:yyyyMMdd_HHmmss}.html");
        await HtmlReportGenerator.GenerateAsync(reportPath, metadata);
        Console.WriteLine($"[Report] Generated: {reportPath}");

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

        scenarioContext["ScenarioStartedAt"] = DateTime.UtcNow;

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

    [AfterStep]
    public async Task AfterStep(ScenarioContext scenarioContext)
    {
        if (!scenarioContext.TryGetValue("ReportSteps", out List<StepResult>? steps))
        {
            steps = [];
            scenarioContext["ReportSteps"] = steps;
        }

        // Compute step duration
        DateTime stepStartedAt;
        if (scenarioContext.TryGetValue("StepStartedAt", out DateTime savedStepStart))
            stepStartedAt = savedStepStart;
        else if (scenarioContext.TryGetValue("ScenarioStartedAt", out DateTime scenarioStart))
            stepStartedAt = scenarioStart;
        else
            stepStartedAt = DateTime.UtcNow;

        var execStatus = scenarioContext.ScenarioExecutionStatus;
        StepStatus status;
        if (execStatus == ScenarioExecutionStatus.OK)
            status = StepStatus.Passed;
        else if (steps.All(s => s.Status != StepStatus.Failed))
            status = StepStatus.Failed;
        else
            status = StepStatus.Skipped;

        var screenshots = new List<ProfileScreenshot>();
        try
        {
            var page = scenarioContext.GetPage();
            var originalViewport = page.ViewportSize;

            foreach (var profile in DisplayProfiles.Default)
            {
                await page.SetViewportSizeAsync(profile.Width, profile.Height);
                await page.EmulateMediaAsync(new PageEmulateMediaOptions
                {
                    ColorScheme = profile.ColorScheme == Reporting.ColorScheme.Dark
                        ? Microsoft.Playwright.ColorScheme.Dark
                        : Microsoft.Playwright.ColorScheme.Light
                });

                var data = await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    FullPage = true,
                    Type = ScreenshotType.Jpeg,
                    Quality = 50
                });

                screenshots.Add(new ProfileScreenshot
                {
                    ProfileName = profile.Name,
                    Data = data
                });
            }

            // Restore original state
            if (originalViewport is not null)
                await page.SetViewportSizeAsync(originalViewport.Width, originalViewport.Height);
            await page.EmulateMediaAsync(new PageEmulateMediaOptions
            {
                ColorScheme = Microsoft.Playwright.ColorScheme.Light
            });
        }
        catch { }

        var stepDuration = DateTime.UtcNow - stepStartedAt;

        steps.Add(new StepResult
        {
            Keyword = scenarioContext.StepContext.StepInfo.StepDefinitionType.ToString(),
            Text = scenarioContext.StepContext.StepInfo.Text,
            Status = status,
            Error = status == StepStatus.Failed ? scenarioContext.TestError?.Message : null,
            StackTrace = status == StepStatus.Failed ? scenarioContext.TestError?.StackTrace : null,
            Duration = stepDuration,
            Screenshots = screenshots
        });

        // Mark start of next step
        scenarioContext["StepStartedAt"] = DateTime.UtcNow;
    }

    [AfterScenario]
    public async Task AfterScenario(ScenarioContext scenarioContext, FeatureContext featureContext)
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

        var steps = scenarioContext.TryGetValue("ReportSteps", out List<StepResult>? s) ? s : [];

        var scenarioStartedAt = scenarioContext.TryGetValue("ScenarioStartedAt", out DateTime startTime)
            ? startTime
            : DateTime.UtcNow;

        var result = new ScenarioResult
        {
            Title = scenarioContext.ScenarioInfo.Title,
            Tags = scenarioContext.ScenarioInfo.Tags,
            FeatureTitle = featureContext.FeatureInfo.Title,
            StartedAt = scenarioStartedAt
        };
        result.Steps.AddRange(steps);
        TestReportCollector.AddScenario(result);

        try
        {
            var browserContext = scenarioContext.GetBrowserContext();
            await browserContext.CloseAsync();
        }
        catch { }
    }
}
