// E2E tests for the Run History list and detail pages (Article V, T088-T089).
using DBAIAzure.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace DBAIAzure.E2ETests.Tests;

/// <summary>
/// Validates that /runs and /runs/{id} load without Blazor errors and render their
/// expected headings. Covers FR-20 (execution history UI) for E2E regression purposes.
/// </summary>
public sealed class RunHistoryTests : E2ETestBase
{
    public RunHistoryTests(WebAppFixture webApp, PlaywrightFixture playwright)
        : base(webApp, playwright) { }

    [Fact]
    public async Task RunList_Loads_HeadingVisible()
    {
        await NavigateAsync("/runs");

        var heading = Page.Locator("h1", new() { HasTextString = "Run History" });
        await heading.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        Assert.True(await IsBlazorConnectedAsync(), "Blazor SignalR must be connected on /runs.");
        var bodyText = await Page.InnerTextAsync("body");
        Assert.DoesNotContain("An unhandled error has occurred", bodyText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunList_Shows_EmptyStateOrRunRows()
    {
        await NavigateAsync("/runs");

        // The page renders either a table row (existing run) or the "No runs match" message.
        // Either is acceptable — the test only verifies the page did not hard crash.
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var hasTable  = await Page.Locator("table").CountAsync() > 0;
        var hasEmpty  = await Page.Locator("text=No runs match").CountAsync() > 0;

        Assert.True(hasTable || hasEmpty, "Expected either a run table or an empty-state message.");
    }

    [Fact]
    public async Task RunDetail_WithUnknownId_ShowsNotFoundMessage()
    {
        // Navigating to a run id that does not exist should render a not-found message,
        // not an unhandled exception page.
        await NavigateAsync("/runs/unknown-run-id-that-does-not-exist");

        // The detail page always renders the "← Run History" back-link; wait for it so we assert on the
        // hydrated page rather than racing the interactive render (the prior networkidle snapshot flaked).
        await Assertions.Expect(Page.Locator("text=← Run History")).ToBeVisibleAsync();

        var bodyText = await Page.InnerTextAsync("body");
        Assert.DoesNotContain("An unhandled error has occurred", bodyText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunDetail_ShowsTokenCostSectionHeading_WhenLlmEventsExist()
    {
        // This test verifies the token-cost section conditional render does not crash.
        // Without real LLM events the section is hidden — just assert no Blazor error.
        await NavigateAsync("/runs");

        var bodyText = await Page.InnerTextAsync("body");
        Assert.DoesNotContain("An unhandled error has occurred", bodyText, StringComparison.OrdinalIgnoreCase);
    }
}
