using LocalTranscriber.Tests.E2E.Hooks;
using LocalTranscriber.Tests.E2E.Support;
using Microsoft.Playwright;
using Reqnroll;

namespace LocalTranscriber.Tests.E2E.StepDefinitions;

[Binding]
public class CommonSteps
{
    private readonly ScenarioContext _scenarioContext;

    public CommonSteps(ScenarioContext scenarioContext) => _scenarioContext = scenarioContext;

    [Given("I am on the home page")]
    public async Task GivenIAmOnTheHomePage()
    {
        var homePage = _scenarioContext.GetHomePage();
        var baseUrl = _scenarioContext.GetBaseUrl();
        await homePage.NavigateAsync(baseUrl);
    }

    [When("I upload a test audio file")]
    public async Task WhenIUploadATestAudioFile()
    {
        var tempWav = TestAudioHelper.CreateTempSilenceWav();
        _scenarioContext["TempAudioFile"] = tempWav;
        var homePage = _scenarioContext.GetHomePage();
        await homePage.UploadFileAsync(tempWav);
    }

    [When("I click the transcribe button")]
    public async Task WhenIClickTheTranscribeButton()
    {
        var homePage = _scenarioContext.GetHomePage();
        await homePage.ClickTranscribeAsync();
    }

    [When("I click the reset button")]
    public async Task WhenIClickTheResetButton()
    {
        var homePage = _scenarioContext.GetHomePage();
        await homePage.ClickResetAsync();
    }

    [When("I reload the page")]
    public async Task WhenIReloadThePage()
    {
        var page = _scenarioContext.GetPage();
        await page.ReloadAsync();
        await page.WaitForSelectorAsync(Selectors.ScreenRoot, new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30_000
        });
    }

    [Then("the page should be loaded")]
    public async Task ThenThePageShouldBeLoaded()
    {
        var page = _scenarioContext.GetPage();
        await Assertions.Expect(page.Locator(Selectors.ScreenRoot)).ToBeVisibleAsync();
    }

    [AfterScenario]
    public void CleanupTempFiles()
    {
        if (_scenarioContext.TryGetValue("TempAudioFile", out string? tempPath) && File.Exists(tempPath))
        {
            try { File.Delete(tempPath); } catch { }
        }
    }
}
