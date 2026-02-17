using LocalTranscriber.Tests.E2E.Hooks;
using LocalTranscriber.Tests.E2E.Support;
using Microsoft.Playwright;
using Reqnroll;
using Xunit;

namespace LocalTranscriber.Tests.E2E.StepDefinitions;

[Binding]
public class TranscriptionFlowSteps
{
    private readonly ScenarioContext _scenarioContext;

    public TranscriptionFlowSteps(ScenarioContext scenarioContext) => _scenarioContext = scenarioContext;

    [Given("the server transcription API is mocked")]
    public async Task GivenTheServerTranscriptionApiIsMocked()
    {
        var page = _scenarioContext.GetPage();

        // Mock the workflow transcribe endpoint
        await page.RouteAsync("**/api/workflow/transcribe", async route =>
        {
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = """
                {
                    "rawText": "This is a test transcription from mocked API.",
                    "segments": [
                        {
                            "startSeconds": 0,
                            "endSeconds": 1,
                            "text": "This is a test transcription from mocked API.",
                            "speaker": null,
                            "words": []
                        }
                    ]
                }
                """
            });
        });

        // Also mock the transcriptions API (used by server mode)
        await page.RouteAsync("**/api/transcriptions", async route =>
        {
            if (route.Request.Method == "POST")
            {
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 202,
                    ContentType = "application/json",
                    Body = """{"jobId": "test-job-001"}"""
                });
            }
            else
            {
                await route.ContinueAsync();
            }
        });

        // Mock polling for transcription result
        await page.RouteAsync("**/api/transcriptions/test-job-001", async route =>
        {
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = """
                {
                    "type": "completed",
                    "percent": 100,
                    "message": "Done",
                    "rawText": "This is a test transcription from mocked API.",
                    "formattedText": "# Test Transcription\n\nThis is a test transcription from mocked API.",
                    "speakerLabeledText": "Speaker 1: This is a test transcription from mocked API.",
                    "formatterOutput": "Formatted output here."
                }
                """
            });
        });
    }

    [Given("the client transcription is mocked")]
    public async Task GivenTheClientTranscriptionIsMocked()
    {
        var page = _scenarioContext.GetPage();

        // Mock all browser-side methods called by the workflow engine's step handlers:
        // - transcribeAudio: called by stepHandlers.transcribe
        // - buildSpeakerLabeledTranscript: called by stepHandlers.speakerLabels
        // - formatWithWebLlm: called by runLlmStep (used by llmFormat, llmTransform, summarize, etc.)
        await page.EvaluateAsync("""
            (function() {
                const browser = window.localTranscriberBrowser;
                if (!browser) return;

                browser.transcribeAudio = async function(audio, model, language, onProgress) {
                    if (onProgress) onProgress(100, "Mock transcription complete");
                    return {
                        text: "This is a mocked client transcription.",
                        segments: [{
                            startSeconds: 0,
                            endSeconds: 1,
                            text: "This is a mocked client transcription.",
                            speaker: null,
                            words: []
                        }]
                    };
                };

                browser.buildSpeakerLabeledTranscript = function(segments, rawText) {
                    return {
                        text: "Speaker 1: " + (rawText || "This is a mocked client transcription."),
                        detectedSpeakerCount: 1
                    };
                };

                browser.formatWithWebLlm = async function(model, format, lang, prompt, onProgress, opts) {
                    if (onProgress) onProgress(100, "Mock LLM complete");
                    return "# Transcription\n\nThis is a mocked client transcription.";
                };
            })();
        """);
    }

    [When("I wait for transcription to complete")]
    public async Task WhenIWaitForTranscriptionToComplete()
    {
        var homePage = _scenarioContext.GetHomePage();
        await homePage.WaitForResultsAsync(60_000);
    }

    [Given("the server transcription API returns an error")]
    public async Task GivenTheServerTranscriptionApiReturnsAnError()
    {
        var page = _scenarioContext.GetPage();

        // The server transcription flow calls /api/workflow/transcribe synchronously.
        // Return a 500 so serverTranscribe() throws, which emits isError progress.
        await page.RouteAsync("**/api/workflow/transcribe", async route =>
        {
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 500,
                ContentType = "application/json",
                Body = """{"error": "Test transcription error: model not found"}"""
            });
        });
    }

    [When("I wait for the error state")]
    public async Task WhenIWaitForTheErrorState()
    {
        var page = _scenarioContext.GetPage();
        // Wait for an error-box to appear (shown during processing errors)
        await page.Locator(".error-box").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30_000
        });
    }

    [Then("an error message should be visible")]
    public async Task ThenAnErrorMessageShouldBeVisible()
    {
        var page = _scenarioContext.GetPage();
        await Assertions.Expect(page.Locator(".error-box")).ToBeVisibleAsync();
    }

    [Then("the result should contain text")]
    public async Task ThenTheResultShouldContainText()
    {
        var results = _scenarioContext.GetResultsPage();
        var content = await results.GetActiveTabContentAsync();
        Assert.False(string.IsNullOrWhiteSpace(content), "Expected result to contain text");
        Assert.NotEqual("(empty)", content.Trim());
    }

    [Then("the {string} tab should show content")]
    public async Task ThenTheTabShouldShowContent(string tabName)
    {
        var results = _scenarioContext.GetResultsPage();
        await results.SwitchToTabAsync(tabName);
        // Small wait for content to render
        await Task.Delay(500);
        var content = await results.GetActiveTabContentAsync();
        Assert.False(string.IsNullOrWhiteSpace(content), $"Expected {tabName} tab to contain text");
    }
}
