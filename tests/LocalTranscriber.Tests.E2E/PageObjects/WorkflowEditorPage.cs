using Microsoft.Playwright;
using LocalTranscriber.Tests.E2E.Support;

namespace LocalTranscriber.Tests.E2E.PageObjects;

public class WorkflowEditorPage
{
    private readonly IPage _page;

    public WorkflowEditorPage(IPage page) => _page = page;

    public async Task ExpandAsync()
    {
        var editor = _page.Locator(Selectors.WorkflowEditor);
        var classAttr = await editor.GetAttributeAsync("class") ?? "";
        if (classAttr.Contains("collapsed"))
        {
            await _page.Locator(Selectors.WorkflowHeader).ClickAsync();
            await _page.Locator(Selectors.WorkflowSelect).WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible
            });
        }
    }

    public async Task CollapseAsync()
    {
        var editor = _page.Locator(Selectors.WorkflowEditor);
        var classAttr = await editor.GetAttributeAsync("class") ?? "";
        if (classAttr.Contains("expanded"))
        {
            await _page.Locator(Selectors.WorkflowHeader).ClickAsync();
            await _page.WaitForTimeoutAsync(300);
        }
    }

    public async Task<string> GetSelectedWorkflowNameAsync()
    {
        var select = _page.Locator(Selectors.WorkflowSelect);
        return await select.InputValueAsync();
    }

    public async Task<int> GetWorkflowOptionCountAsync()
    {
        return await _page.Locator($"{Selectors.WorkflowSelect} option").CountAsync();
    }

    public async Task DuplicateWorkflowAsync()
    {
        var countBefore = await GetWorkflowOptionCountAsync();
        await _page.Locator(Selectors.WorkflowDuplicate).ClickAsync();
        // Wait for Blazor to re-render with new option
        await WaitForOptionCountChangeAsync(countBefore);
    }

    public async Task CreateNewWorkflowAsync()
    {
        var countBefore = await GetWorkflowOptionCountAsync();
        await _page.Locator(Selectors.WorkflowNew).ClickAsync();
        // Wait for Blazor to re-render with new option
        await WaitForOptionCountChangeAsync(countBefore);
    }

    public async Task DeleteWorkflowAsync()
    {
        var countBefore = await GetWorkflowOptionCountAsync();
        await _page.Locator(Selectors.WorkflowDelete).ClickAsync();
        // Wait for option count to decrease
        await WaitForOptionCountChangeAsync(countBefore);
    }

    public async Task<bool> IsDeleteButtonVisibleAsync()
    {
        return await _page.Locator(Selectors.WorkflowDelete).IsVisibleAsync();
    }

    public async Task AddStepAsync(string stepType)
    {
        var countBefore = await GetStepCountAsync();
        await _page.Locator(Selectors.AddStepButton).ClickAsync();
        await _page.Locator(Selectors.AddStepMenu).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });
        // Click the step type option that matches
        await _page.Locator($"{Selectors.StepTypeOption}:has-text('{stepType}')").First.ClickAsync();
        // Wait for step count to change
        await WaitForStepCountChangeAsync(countBefore);
    }

    public async Task RemoveStepAsync(int index)
    {
        var countBefore = await GetStepCountAsync();
        var steps = _page.Locator(Selectors.WorkflowStep);
        var step = steps.Nth(index);
        await step.Locator(Selectors.StepRemoveButton).ClickAsync();
        // Wait for step count to change
        await WaitForStepCountChangeAsync(countBefore);
    }

    public async Task MoveStepDownAsync(int index)
    {
        var steps = _page.Locator(Selectors.WorkflowStep);
        var step = steps.Nth(index);
        await step.Locator(Selectors.StepMoveDownButton).ClickAsync();
        // Wait for Blazor re-render
        await _page.WaitForTimeoutAsync(300);
    }

    public async Task MoveStepUpAsync(int index)
    {
        var steps = _page.Locator(Selectors.WorkflowStep);
        var step = steps.Nth(index);
        await step.Locator(Selectors.StepMoveUpButton).ClickAsync();
        // Wait for Blazor re-render
        await _page.WaitForTimeoutAsync(300);
    }

    public async Task SwitchToSimpleViewAsync()
    {
        await _page.Locator(Selectors.ViewSimple).ClickAsync();
        // Wait for Blazor to re-render the active class
        await _page.WaitForTimeoutAsync(300);
    }

    public async Task SwitchToPhaseViewAsync()
    {
        await _page.Locator(Selectors.ViewPhase).ClickAsync();
        // Wait for Blazor to re-render the active class
        await _page.WaitForTimeoutAsync(300);
    }

    public async Task<int> GetStepCountAsync()
    {
        // Simple view uses .workflow-step, Phase view uses .phase-step
        var count = await _page.Locator(Selectors.WorkflowStep).CountAsync();
        if (count == 0)
            count = await _page.Locator(Selectors.PhaseStep).CountAsync();
        return count;
    }

    public async Task<string> GetStepNameAsync(int index)
    {
        var steps = _page.Locator(Selectors.WorkflowStep);
        var step = steps.Nth(index);
        return await step.Locator(".step-name").InnerTextAsync();
    }

    public async Task CreateFromTemplateAsync()
    {
        await _page.Locator(Selectors.WorkflowTemplate).ClickAsync();
        await _page.Locator(Selectors.PresetPicker).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });
        // Click the first preset option
        await _page.Locator(Selectors.PresetOption).First.ClickAsync();
        // Wait for Blazor to process the template
        await _page.WaitForTimeoutAsync(500);
    }

    public async Task ClickStepHeaderAsync(int index)
    {
        var steps = _page.Locator(Selectors.WorkflowStep);
        var step = steps.Nth(index);
        await step.Locator(Selectors.StepHeader).ClickAsync();
        // Wait for config panel to appear
        await _page.WaitForTimeoutAsync(300);
    }

    public async Task<bool> IsStepConfigVisibleAsync(int index)
    {
        var steps = _page.Locator(Selectors.WorkflowStep);
        var step = steps.Nth(index);
        return await step.Locator(Selectors.StepConfig).IsVisibleAsync();
    }

    private async Task WaitForOptionCountChangeAsync(int previousCount, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var current = await GetWorkflowOptionCountAsync();
            if (current != previousCount)
                return;
            await _page.WaitForTimeoutAsync(100);
        }
    }

    private async Task WaitForStepCountChangeAsync(int previousCount, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var current = await GetStepCountAsync();
            if (current != previousCount)
                return;
            await _page.WaitForTimeoutAsync(100);
        }
    }
}
