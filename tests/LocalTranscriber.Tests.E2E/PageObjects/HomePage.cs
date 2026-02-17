using Microsoft.Playwright;
using LocalTranscriber.Tests.E2E.Support;

namespace LocalTranscriber.Tests.E2E.PageObjects;

public class HomePage
{
    private readonly IPage _page;

    public HomePage(IPage page) => _page = page;

    public async Task NavigateAsync(string baseUrl)
    {
        await _page.GotoAsync(baseUrl);
        await _page.WaitForSelectorAsync(Selectors.ScreenRoot, new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30_000
        });
    }

    public async Task UploadFileAsync(string filePath)
    {
        // Use FileChooser API to simulate real user interaction — Blazor's InputFile
        // requires native browser events that SetInputFilesAsync doesn't always trigger
        var fileChooserTask = _page.WaitForFileChooserAsync();
        await _page.Locator(Selectors.UploadLabel).ClickAsync();
        var fileChooser = await fileChooserTask;
        await fileChooser.SetFilesAsync(filePath);

        // Wait for Blazor to process the upload and show the transcribe button
        await _page.Locator(Selectors.TranscribeButton).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10_000
        });
    }

    public async Task ClickTranscribeAsync()
    {
        var btn = _page.Locator(Selectors.TranscribeButton);
        await btn.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10_000
        });
        await btn.ClickAsync();
    }

    public async Task ClickResetAsync()
    {
        await _page.Locator(Selectors.ResetButton).ClickAsync();
        // Wait for Blazor to re-render back to capture stage
        await _page.Locator(Selectors.MinimalCapture).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10_000
        });
    }

    public async Task ClickStudioModeAsync()
    {
        await _page.Locator(Selectors.StudioModeButton).ClickAsync();
        // Wait for studio grid to render
        await _page.Locator(Selectors.StudioGrid).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5_000
        });
    }

    public async Task ClickMinimalModeAsync()
    {
        await _page.Locator(Selectors.MinimalModeButton).ClickAsync();
        // Wait for minimal mode to render
        await _page.Locator(Selectors.ScreenRoot + ".minimal").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5_000
        });
    }

    public async Task<bool> IsInStudioModeAsync()
    {
        var root = _page.Locator(Selectors.ScreenRoot);
        var classAttr = await root.GetAttributeAsync("class") ?? "";
        return classAttr.Contains("studio");
    }

    public async Task<bool> IsInMinimalModeAsync()
    {
        return !await IsInStudioModeAsync();
    }

    public async Task<string> GetCurrentStageAsync()
    {
        if (await _page.Locator(Selectors.MinimalCapture).IsVisibleAsync())
            return "capture";
        if (await _page.Locator(Selectors.MinimalProcessing).IsVisibleAsync())
            return "processing";
        if (await _page.Locator(Selectors.MinimalResults).IsVisibleAsync())
            return "results";
        if (await _page.Locator(Selectors.StudioGrid).IsVisibleAsync())
            return "studio";
        return "unknown";
    }

    public async Task OpenSettingsAsync()
    {
        var settings = _page.Locator(Selectors.SettingsPanel);
        var isOpen = await settings.GetAttributeAsync("open");
        if (isOpen is null)
        {
            // Use the settings-panel-toggle class to target only the direct summary
            await settings.Locator("summary.settings-panel-toggle").ClickAsync();
        }
    }

    public async Task CloseSettingsAsync()
    {
        var settings = _page.Locator(Selectors.SettingsPanel);
        var isOpen = await settings.GetAttributeAsync("open");
        if (isOpen is not null)
        {
            await settings.Locator("summary.settings-panel-toggle").ClickAsync();
        }
    }

    public async Task WaitForResultsAsync(float timeoutMs = 60_000)
    {
        await _page.Locator(Selectors.MinimalResults).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }

    public async Task<int> GetStudioCardCountAsync()
    {
        await _page.Locator(Selectors.StudioCard).First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5_000
        });
        return await _page.Locator(Selectors.StudioCard).CountAsync();
    }
}
