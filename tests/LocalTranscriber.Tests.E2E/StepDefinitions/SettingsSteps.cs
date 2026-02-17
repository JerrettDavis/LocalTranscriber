using LocalTranscriber.Tests.E2E.Hooks;
using LocalTranscriber.Tests.E2E.Support;
using Microsoft.Playwright;
using Reqnroll;
using Xunit;

namespace LocalTranscriber.Tests.E2E.StepDefinitions;

[Binding]
public class SettingsSteps
{
    private readonly ScenarioContext _scenarioContext;

    public SettingsSteps(ScenarioContext scenarioContext) => _scenarioContext = scenarioContext;

    [When("I open the settings panel")]
    public async Task WhenIOpenTheSettingsPanel()
    {
        var settings = _scenarioContext.GetSettingsPanel();
        await settings.OpenAsync();
    }

    [When("I close the settings panel")]
    public async Task WhenICloseTheSettingsPanel()
    {
        var settings = _scenarioContext.GetSettingsPanel();
        await settings.CloseAsync();
    }

    [When("I open the advanced settings")]
    public async Task WhenIOpenTheAdvancedSettings()
    {
        var settings = _scenarioContext.GetSettingsPanel();
        await settings.OpenAdvancedSettingsAsync();
    }

    [Given("the advanced settings are open")]
    public async Task GivenTheAdvancedSettingsAreOpen()
    {
        var settings = _scenarioContext.GetSettingsPanel();
        await settings.OpenAdvancedSettingsAsync();
    }

    [When("I save the tuning settings")]
    public async Task WhenISaveTheTuningSettings()
    {
        var settings = _scenarioContext.GetSettingsPanel();
        await settings.SaveTuningAsync();
    }

    [When("I reset the tuning settings")]
    public async Task WhenIResetTheTuningSettings()
    {
        var settings = _scenarioContext.GetSettingsPanel();
        await settings.ResetTuningAsync();
    }

    [Then("the settings panel should be open")]
    public async Task ThenTheSettingsPanelShouldBeOpen()
    {
        var settings = _scenarioContext.GetSettingsPanel();
        var isOpen = await settings.IsOpenAsync();
        Assert.True(isOpen, "Expected settings panel to be open");
    }

    [Then("the settings panel should be closed")]
    public async Task ThenTheSettingsPanelShouldBeClosed()
    {
        var settings = _scenarioContext.GetSettingsPanel();
        var isOpen = await settings.IsOpenAsync();
        Assert.False(isOpen, "Expected settings panel to be closed");
    }

    [Then("the advanced settings should be visible")]
    public async Task ThenTheAdvancedSettingsShouldBeVisible()
    {
        var settings = _scenarioContext.GetSettingsPanel();
        var visible = await settings.IsAdvancedSettingsVisibleAsync();
        Assert.True(visible, "Expected advanced settings to be visible");
    }

    [Then("the Server LLM Providers section should be visible")]
    public async Task ThenTheServerLlmProvidersSectionShouldBeVisible()
    {
        var settings = _scenarioContext.GetSettingsPanel();
        var visible = await settings.IsServerLlmProvidersVisibleAsync();
        Assert.True(visible, "Expected Server LLM Providers to be visible on client");
    }

    [Then("the Server LLM Providers section should not be visible")]
    public async Task ThenTheServerLlmProvidersSectionShouldNotBeVisible()
    {
        var settings = _scenarioContext.GetSettingsPanel();
        var visible = await settings.IsServerLlmProvidersVisibleAsync();
        Assert.False(visible, "Expected Server LLM Providers to be hidden in standalone client mode");
    }

    [When("I open the prompt editor")]
    public async Task WhenIOpenThePromptEditor()
    {
        var settings = _scenarioContext.GetSettingsPanel();
        await settings.OpenPromptEditorAsync();
    }

    [Then("the prompt editor should be visible")]
    public async Task ThenThePromptEditorShouldBeVisible()
    {
        var settings = _scenarioContext.GetSettingsPanel();
        var visible = await settings.IsPromptEditorVisibleAsync();
        Assert.True(visible, "Expected prompt editor to be visible");
    }

    [When("I click the YouTube button")]
    public async Task WhenIClickTheYouTubeButton()
    {
        var page = _scenarioContext.GetPage();
        await page.Locator("button[title='Transcribe YouTube video']").ClickAsync();
        await page.WaitForTimeoutAsync(300);
    }

    [Then("the YouTube URL input should be visible")]
    public async Task ThenTheYouTubeUrlInputShouldBeVisible()
    {
        var page = _scenarioContext.GetPage();
        await Assertions.Expect(page.Locator(".youtube-url-panel")).ToBeVisibleAsync();
    }

    [Then("the YouTube URL input should not be visible")]
    public async Task ThenTheYouTubeUrlInputShouldNotBeVisible()
    {
        var page = _scenarioContext.GetPage();
        await Assertions.Expect(page.Locator(".youtube-url-panel")).Not.ToBeVisibleAsync();
    }

    [Then("the speed priority toggle should be visible")]
    public async Task ThenTheSpeedPriorityToggleShouldBeVisible()
    {
        var page = _scenarioContext.GetPage();
        await Assertions.Expect(page.Locator(Selectors.SpeedPriorityToggle)).ToBeVisibleAsync();
    }

    [Then("the session history accordion should be visible")]
    public async Task ThenTheSessionHistoryAccordionShouldBeVisible()
    {
        var page = _scenarioContext.GetPage();
        await Assertions.Expect(page.Locator(Selectors.SessionHistory)).ToBeVisibleAsync();
    }

    [When("I open the diagnostics section")]
    public async Task WhenIOpenTheDiagnosticsSection()
    {
        var settings = _scenarioContext.GetSettingsPanel();
        await settings.OpenDiagnosticsAsync();
    }

    [Then("the diagnostics section should be visible")]
    public async Task ThenTheDiagnosticsSectionShouldBeVisible()
    {
        var settings = _scenarioContext.GetSettingsPanel();
        var visible = await settings.IsDiagnosticsVisibleAsync();
        Assert.True(visible, "Expected diagnostics section to be visible");
    }
}
