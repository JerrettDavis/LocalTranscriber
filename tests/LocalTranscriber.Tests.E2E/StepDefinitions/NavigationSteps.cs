using LocalTranscriber.Tests.E2E.Hooks;
using LocalTranscriber.Tests.E2E.Support;
using Microsoft.Playwright;
using Reqnroll;
using Xunit;

namespace LocalTranscriber.Tests.E2E.StepDefinitions;

[Binding]
public class NavigationSteps
{
    private readonly ScenarioContext _scenarioContext;

    public NavigationSteps(ScenarioContext scenarioContext) => _scenarioContext = scenarioContext;

    [Given("I am in minimal mode")]
    public async Task GivenIAmInMinimalMode()
    {
        var homePage = _scenarioContext.GetHomePage();
        var isMinimal = await homePage.IsInMinimalModeAsync();
        Assert.True(isMinimal, "Expected to be in minimal mode");
    }

    [Given("I am in studio mode")]
    public async Task GivenIAmInStudioMode()
    {
        var homePage = _scenarioContext.GetHomePage();
        if (await homePage.IsInMinimalModeAsync())
        {
            await homePage.ClickStudioModeAsync();
        }
    }

    [When("I switch to studio mode")]
    public async Task WhenISwitchToStudioMode()
    {
        var homePage = _scenarioContext.GetHomePage();
        await homePage.ClickStudioModeAsync();
    }

    [When("I switch to minimal mode")]
    public async Task WhenISwitchToMinimalMode()
    {
        var homePage = _scenarioContext.GetHomePage();
        await homePage.ClickMinimalModeAsync();
    }

    [Then("I should see the studio grid")]
    public async Task ThenIShouldSeeTheStudioGrid()
    {
        var page = _scenarioContext.GetPage();
        await Assertions.Expect(page.Locator(Selectors.StudioGrid)).ToBeVisibleAsync();
    }

    [Then("I should see minimal mode")]
    public async Task ThenIShouldSeeMinimalMode()
    {
        var homePage = _scenarioContext.GetHomePage();
        var isMinimal = await homePage.IsInMinimalModeAsync();
        Assert.True(isMinimal, "Expected to be in minimal mode");
    }

    [Then("I should see {int} studio cards")]
    public async Task ThenIShouldSeeStudioCards(int expectedCount)
    {
        var homePage = _scenarioContext.GetHomePage();
        var count = await homePage.GetStudioCardCountAsync();
        Assert.Equal(expectedCount, count);
    }

    [Then("I should be at the {string} stage")]
    public async Task ThenIShouldBeAtTheStage(string expectedStage)
    {
        var homePage = _scenarioContext.GetHomePage();
        var stage = await homePage.GetCurrentStageAsync();
        Assert.Equal(expectedStage, stage);
    }

    [Then("the record button should be visible")]
    public async Task ThenTheRecordButtonShouldBeVisible()
    {
        var page = _scenarioContext.GetPage();
        await Assertions.Expect(page.Locator(Selectors.RecordButton)).ToBeVisibleAsync();
    }
}
