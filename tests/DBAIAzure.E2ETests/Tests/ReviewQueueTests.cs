// E2E tests for the Review Queue page (Article V, T090).
using DBAIAzure.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace DBAIAzure.E2ETests.Tests;

/// <summary>
/// Validates that /review-queue loads without Blazor errors and renders its expected UI.
/// Covers FR-20.1 (review queue — pending approvals) for E2E regression.
/// </summary>
public sealed class ReviewQueueTests : E2ETestBase
{
    public ReviewQueueTests(WebAppFixture webApp, PlaywrightFixture playwright)
        : base(webApp, playwright) { }

    [Fact]
    public async Task ReviewQueue_Loads_HeadingVisible()
    {
        await NavigateAsync("/review-queue");

        var heading = Page.Locator("h1", new() { HasTextString = "Review Queue" });
        await heading.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        Assert.True(await IsBlazorConnectedAsync(), "Blazor SignalR must be connected on /review-queue.");
        var bodyText = await Page.InnerTextAsync("body");
        Assert.DoesNotContain("An unhandled error has occurred", bodyText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReviewQueue_EmptyState_ShowsNoRunsMessage()
    {
        await NavigateAsync("/review-queue");

        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // When no runs are paused, the empty-state message must be visible.
        // If runs ARE paused (unlikely in CI), the run card cards are present instead.
        var emptyMsg   = await Page.Locator("text=No runs are currently awaiting").CountAsync();
        var hasPendingCard = await Page.Locator("button:has-text('Approve')").CountAsync();

        Assert.True(emptyMsg > 0 || hasPendingCard > 0,
            "Expected either empty-state message or a pending approval card.");
    }

    [Fact]
    public async Task ReviewQueue_NavigationFromMainNav_ReachesPage()
    {
        // Verify the nav link in MainLayout reaches the review queue page.
        await NavigateAsync("/");

        var navLink = Page.Locator("nav a", new() { HasTextString = "Review Queue" });
        await navLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var heading = Page.Locator("h1", new() { HasTextString = "Review Queue" });
        await heading.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });

        Assert.True(await IsBlazorConnectedAsync());
    }
}
