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

    /// <summary>
    /// T087: When a workflow run is suspended at a HITL approval gate and the operator
    /// clicks Approve in the Review Queue, the run transitions out of the Paused state.
    ///
    /// Full flow requires a workflow with a HumanApproval node that is running and Paused.
    /// In CI this runs against the test SQLite DB which may or may not have a paused run;
    /// the test validates the approval UI path when runs exist, and passes gracefully when none do.
    /// </summary>
    [Fact]
    public async Task OperatorApprovalFlow_WhenPausedRunExists_ApproveButtonIsClickable()
    {
        await NavigateAsync("/review-queue");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var approveButtons = await Page.Locator("button:has-text('Approve')").AllAsync();
        if (approveButtons.Count == 0)
        {
            // No paused runs in this CI run — verify the empty-state message is visible instead.
            var emptyMsg = Page.Locator("text=No runs are currently awaiting");
            await emptyMsg.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
            return;
        }

        // At least one paused run: click Approve on the first item. The queue should refresh
        // and either remove the approved item (if orchestrator resolves it) or keep showing it.
        // No unhandled exceptions must occur.
        await approveButtons[0].ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var bodyText = await Page.InnerTextAsync("body");
        Assert.DoesNotContain("An unhandled error has occurred", bodyText, StringComparison.OrdinalIgnoreCase);
        Assert.True(await IsBlazorConnectedAsync(), "Blazor SignalR must remain connected after approval.");
    }
}
