using LocalTranscriber.Tests.E2E.Hooks;
using LocalTranscriber.Tests.E2E.Support;
using Microsoft.Playwright;
using Reqnroll;
using Xunit;

namespace LocalTranscriber.Tests.E2E.StepDefinitions;

[Binding]
public class WorkflowEditorSteps
{
    private readonly ScenarioContext _scenarioContext;

    public WorkflowEditorSteps(ScenarioContext scenarioContext) => _scenarioContext = scenarioContext;

    [Given("the settings panel is open")]
    public async Task GivenTheSettingsPanelIsOpen()
    {
        var homePage = _scenarioContext.GetHomePage();
        await homePage.OpenSettingsAsync();
    }

    [Given("the workflow editor is expanded")]
    public async Task GivenTheWorkflowEditorIsExpanded()
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        await editor.ExpandAsync();
    }

    [Given("the default workflow is selected")]
    public async Task GivenTheDefaultWorkflowIsSelected()
    {
        // The default workflow is selected by default, just verify
        var page = _scenarioContext.GetPage();
        await Assertions.Expect(page.Locator(Selectors.WorkflowSelect)).ToBeVisibleAsync();
    }

    [Given("I note the current step count")]
    public async Task GivenINoteTheCurrentStepCount()
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        var count = await editor.GetStepCountAsync();
        _scenarioContext["OriginalStepCount"] = count;
    }

    [Given("I note the name of step {int}")]
    public async Task GivenINoteTheNameOfStep(int index)
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        var name = await editor.GetStepNameAsync(index);
        _scenarioContext["NotedStepName"] = name;
    }

    [Given("there is at least one step")]
    public async Task GivenThereIsAtLeastOneStep()
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        var count = await editor.GetStepCountAsync();
        if (count == 0)
        {
            await editor.AddStepAsync("Transcribe");
            _scenarioContext["OriginalStepCount"] = 1;
        }
    }

    [Given("there are at least 2 steps")]
    public async Task GivenThereAreAtLeast2Steps()
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        var count = await editor.GetStepCountAsync();
        while (count < 2)
        {
            await editor.AddStepAsync("Transcribe");
            count++;
        }
    }

    [When("I duplicate the workflow")]
    public async Task WhenIDuplicateTheWorkflow()
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        var optionsBefore = await editor.GetWorkflowOptionCountAsync();
        _scenarioContext["OriginalOptionCount"] = optionsBefore;
        await editor.DuplicateWorkflowAsync();
    }

    [When("I create a new workflow")]
    public async Task WhenICreateANewWorkflow()
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        if (!_scenarioContext.ContainsKey("OriginalOptionCount"))
        {
            var optionsBefore = await editor.GetWorkflowOptionCountAsync();
            _scenarioContext["OriginalOptionCount"] = optionsBefore;
        }
        await editor.CreateNewWorkflowAsync();
    }

    [When("I delete the current workflow")]
    public async Task WhenIDeleteTheCurrentWorkflow()
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        await editor.DeleteWorkflowAsync();
    }

    [When("I add a {string} step")]
    public async Task WhenIAddAStep(string stepType)
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        await editor.AddStepAsync(stepType);
    }

    [When("I remove the first step")]
    public async Task WhenIRemoveTheFirstStep()
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        await editor.RemoveStepAsync(0);
    }

    [When("I move step {int} down")]
    public async Task WhenIMoveStepDown(int index)
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        await editor.MoveStepDownAsync(index);
    }

    [When("I move step {int} up")]
    public async Task WhenIMoveStepUp(int index)
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        await editor.MoveStepUpAsync(index);
    }

    [When("I collapse the workflow editor")]
    public async Task WhenICollapseTheWorkflowEditor()
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        await editor.CollapseAsync();
    }

    [When("I expand the workflow editor")]
    public async Task WhenIExpandTheWorkflowEditor()
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        await editor.ExpandAsync();
    }

    [When("I select the first workflow")]
    public async Task WhenISelectTheFirstWorkflow()
    {
        var page = _scenarioContext.GetPage();
        var select = page.Locator(Selectors.WorkflowSelect);
        var firstOption = select.Locator("option").First;
        var value = await firstOption.GetAttributeAsync("value") ?? "";
        await select.SelectOptionAsync(value);
        await page.WaitForTimeoutAsync(300);
    }

    [When("I click on step {int} header")]
    public async Task WhenIClickOnStepHeader(int index)
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        await editor.ClickStepHeaderAsync(index);
    }

    [When("I switch to {string} view")]
    public async Task WhenISwitchToView(string viewName)
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        if (viewName.Equals("Phase", StringComparison.OrdinalIgnoreCase))
            await editor.SwitchToPhaseViewAsync();
        else
            await editor.SwitchToSimpleViewAsync();
    }

    [When("I click the template button")]
    public async Task WhenIClickTheTemplateButton()
    {
        var page = _scenarioContext.GetPage();
        await page.Locator(Selectors.WorkflowTemplate).ClickAsync();
    }

    [When("I select the first preset")]
    public async Task WhenISelectTheFirstPreset()
    {
        var page = _scenarioContext.GetPage();
        await page.Locator(Selectors.PresetOption).First.ClickAsync();
    }

    [Then("the default workflow should be selected")]
    public async Task ThenTheDefaultWorkflowShouldBeSelected()
    {
        var page = _scenarioContext.GetPage();
        var select = page.Locator(Selectors.WorkflowSelect);
        var value = await select.InputValueAsync();
        Assert.Equal("default", value);
    }

    [Then("the workflow select should have more options than before")]
    public async Task ThenTheWorkflowSelectShouldHaveMoreOptionsThanBefore()
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        var originalCount = (int)_scenarioContext["OriginalOptionCount"];
        var currentCount = await editor.GetWorkflowOptionCountAsync();
        Assert.True(currentCount > originalCount,
            $"Expected more options than {originalCount}, but got {currentCount}");
    }

    [Then("the workflow select should have the original option count")]
    public async Task ThenTheWorkflowSelectShouldHaveTheOriginalOptionCount()
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        var originalCount = (int)_scenarioContext["OriginalOptionCount"];
        var currentCount = await editor.GetWorkflowOptionCountAsync();
        Assert.Equal(originalCount, currentCount);
    }

    [Then("the delete button should not be visible")]
    public async Task ThenTheDeleteButtonShouldNotBeVisible()
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        var visible = await editor.IsDeleteButtonVisibleAsync();
        Assert.False(visible, "Delete button should not be visible for the default workflow");
    }

    [Then("the step count should have increased by {int}")]
    public async Task ThenTheStepCountShouldHaveIncreasedBy(int increase)
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        var originalCount = (int)_scenarioContext["OriginalStepCount"];
        var currentCount = await editor.GetStepCountAsync();
        Assert.Equal(originalCount + increase, currentCount);
    }

    [Then("the step count should have decreased by {int}")]
    public async Task ThenTheStepCountShouldHaveDecreasedBy(int decrease)
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        var originalCount = (int)_scenarioContext["OriginalStepCount"];
        var currentCount = await editor.GetStepCountAsync();
        Assert.Equal(originalCount - decrease, currentCount);
    }

    [Then("step {int} should have the previously noted name")]
    public async Task ThenStepShouldHaveThePreviouslyNotedName(int index)
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        var notedName = (string)_scenarioContext["NotedStepName"];
        var currentName = await editor.GetStepNameAsync(index);
        Assert.Equal(notedName, currentName);
    }

    [Then("the step {int} config should be visible")]
    public async Task ThenTheStepConfigShouldBeVisible(int index)
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        var visible = await editor.IsStepConfigVisibleAsync(index);
        Assert.True(visible, $"Expected step {index} config to be visible");
    }

    [Then("the phase view should be active")]
    public async Task ThenThePhaseViewShouldBeActive()
    {
        var page = _scenarioContext.GetPage();
        var phaseBtn = page.Locator(Selectors.ViewPhase);
        await Assertions.Expect(phaseBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("active"));
    }

    [Then("the simple view should be active")]
    public async Task ThenTheSimpleViewShouldBeActive()
    {
        var page = _scenarioContext.GetPage();
        var simpleBtn = page.Locator(Selectors.ViewSimple);
        await Assertions.Expect(simpleBtn).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("active"));
    }

    [Then("the workflow should have steps")]
    public async Task ThenTheWorkflowShouldHaveSteps()
    {
        var editor = _scenarioContext.GetWorkflowEditorPage();
        var count = await editor.GetStepCountAsync();
        Assert.True(count > 0, "Expected workflow to have at least one step after creating from template");
    }

    [Then("the workflow editor should be collapsed")]
    public async Task ThenTheWorkflowEditorShouldBeCollapsed()
    {
        var page = _scenarioContext.GetPage();
        var editor = page.Locator(Selectors.WorkflowEditor);
        await Assertions.Expect(editor).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("collapsed"));
    }

    [Then("the workflow editor should be expanded")]
    public async Task ThenTheWorkflowEditorShouldBeExpanded()
    {
        var page = _scenarioContext.GetPage();
        var select = page.Locator(Selectors.WorkflowSelect);
        await Assertions.Expect(select).ToBeVisibleAsync();
    }
}
