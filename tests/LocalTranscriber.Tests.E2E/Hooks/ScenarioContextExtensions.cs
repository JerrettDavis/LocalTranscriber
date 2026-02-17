using Microsoft.Playwright;
using LocalTranscriber.Tests.E2E.PageObjects;
using Reqnroll;

namespace LocalTranscriber.Tests.E2E.Hooks;

public static class ScenarioContextExtensions
{
    private const string PageKey = "PlaywrightPage";
    private const string ContextKey = "BrowserContext";
    private const string BaseUrlKey = "BaseUrl";

    public static void SetPage(this ScenarioContext context, IPage page) =>
        context[PageKey] = page;

    public static IPage GetPage(this ScenarioContext context) =>
        (IPage)context[PageKey];

    public static void SetBrowserContext(this ScenarioContext context, IBrowserContext browserContext) =>
        context[ContextKey] = browserContext;

    public static IBrowserContext GetBrowserContext(this ScenarioContext context) =>
        (IBrowserContext)context[ContextKey];

    public static void SetBaseUrl(this ScenarioContext context, string url) =>
        context[BaseUrlKey] = url;

    public static string GetBaseUrl(this ScenarioContext context) =>
        (string)context[BaseUrlKey];

    public static HomePage GetHomePage(this ScenarioContext context) =>
        new(context.GetPage());

    public static WorkflowEditorPage GetWorkflowEditorPage(this ScenarioContext context) =>
        new(context.GetPage());

    public static SettingsPanel GetSettingsPanel(this ScenarioContext context) =>
        new(context.GetPage());

    public static ResultsPage GetResultsPage(this ScenarioContext context) =>
        new(context.GetPage());
}
