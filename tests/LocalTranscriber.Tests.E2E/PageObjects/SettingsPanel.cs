using Microsoft.Playwright;
using LocalTranscriber.Tests.E2E.Support;

namespace LocalTranscriber.Tests.E2E.PageObjects;

public class SettingsPanel
{
    private readonly IPage _page;

    public SettingsPanel(IPage page) => _page = page;

    public async Task OpenAsync()
    {
        var settings = _page.Locator(Selectors.SettingsPanel);
        var isOpen = await settings.GetAttributeAsync("open");
        if (isOpen is null)
        {
            await settings.Locator("summary.settings-panel-toggle").ClickAsync();
        }
    }

    public async Task CloseAsync()
    {
        var settings = _page.Locator(Selectors.SettingsPanel);
        var isOpen = await settings.GetAttributeAsync("open");
        if (isOpen is not null)
        {
            await settings.Locator("summary.settings-panel-toggle").ClickAsync();
        }
    }

    public async Task<bool> IsOpenAsync()
    {
        var settings = _page.Locator(Selectors.SettingsPanel);
        var isOpen = await settings.GetAttributeAsync("open");
        return isOpen is not null;
    }

    public async Task OpenAdvancedSettingsAsync()
    {
        var advanced = _page.Locator(Selectors.AdvancedSettings);
        var isOpen = await advanced.GetAttributeAsync("open");
        if (isOpen is null)
        {
            await advanced.Locator("summary.settings-accordion-toggle").ClickAsync();
        }
    }

    public async Task<bool> IsAdvancedSettingsVisibleAsync()
    {
        var advanced = _page.Locator(Selectors.AdvancedSettings);
        return await advanced.Locator(".settings-accordion-content").IsVisibleAsync();
    }

    public async Task SaveTuningAsync()
    {
        await _page.Locator($"{Selectors.AdvancedSettings} button:has-text('Save Settings')").ClickAsync();
    }

    public async Task ResetTuningAsync()
    {
        await _page.Locator($"{Selectors.AdvancedSettings} button:has-text('Reset to Defaults')").ClickAsync();
    }

    public async Task<bool> IsServerLlmProvidersVisibleAsync()
    {
        return await _page.Locator(Selectors.ServerLlmProviders).IsVisibleAsync();
    }

    public async Task OpenPromptEditorAsync()
    {
        var promptEditor = _page.Locator(Selectors.PromptEditor);
        var isOpen = await promptEditor.GetAttributeAsync("open");
        if (isOpen is null)
        {
            await promptEditor.Locator("summary.settings-accordion-toggle").ClickAsync();
        }
    }

    public async Task<bool> IsPromptEditorVisibleAsync()
    {
        var promptEditor = _page.Locator(Selectors.PromptEditor);
        return await promptEditor.Locator(".prompt-editor-content").IsVisibleAsync();
    }

    public async Task OpenDiagnosticsAsync()
    {
        var diagnostics = _page.Locator(Selectors.Diagnostics);
        var isOpen = await diagnostics.GetAttributeAsync("open");
        if (isOpen is null)
        {
            await diagnostics.Locator("summary.settings-accordion-toggle").ClickAsync();
        }
    }

    public async Task<bool> IsDiagnosticsVisibleAsync()
    {
        var diagnostics = _page.Locator(Selectors.Diagnostics);
        return await diagnostics.Locator(".settings-accordion-content,.diagnostics-grid,.diagnostics-loading").First.IsVisibleAsync();
    }
}
