using Microsoft.Playwright;
using LocalTranscriber.Tests.E2E.Support;

namespace LocalTranscriber.Tests.E2E.PageObjects;

public class ResultsPage
{
    private readonly IPage _page;

    public ResultsPage(IPage page) => _page = page;

    public async Task SwitchToTabAsync(string tabName)
    {
        await _page.Locator($"{Selectors.ResultTabRow} button:has-text('{tabName}')").ClickAsync();
    }

    public async Task<string> GetActiveTabContentAsync()
    {
        return await _page.Locator(".tab-panel").InnerTextAsync();
    }

    public async Task<string> GetActiveTabNameAsync()
    {
        var buttons = _page.Locator($"{Selectors.ResultTabRow} button");
        var count = await buttons.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var btn = buttons.Nth(i);
            var classAttr = await btn.GetAttributeAsync("class") ?? "";
            if (classAttr.Contains("active"))
                return await btn.InnerTextAsync();
        }
        return "";
    }

    public async Task ClickResetAsync()
    {
        await _page.Locator(Selectors.ResetButton).ClickAsync();
    }
}
