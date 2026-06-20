// Validates the Threads (home) page: connector gear icon, modal open/close, and empty state.
using DBAIAzure.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace DBAIAzure.E2ETests.Tests;

/// <summary>
/// End-to-end tests for the Threads page (/) — the app's home page.
/// Covers empty state, gear icon presence, and connector modal open/close.
/// </summary>
public sealed class ThreadsPageTests : E2ETestBase
{
    public ThreadsPageTests(WebAppFixture webApp, PlaywrightFixture playwright)
        : base(webApp, playwright) { }

    [Fact]
    public async Task ThreadsPage_EmptyState_OrList_IsVisible()
    {
        await NavigateAsync("/");

        // The page renders either a list of past runs or a "no runs yet" empty state.
        // Both are valid; we just assert that the page body contains real content.
        var body = Page.Locator("main");
        await body.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        var text = await body.InnerTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(text), "Threads page body should contain rendered content.");
    }

    [Fact]
    public async Task GearIcon_IsVisible_InHeader()
    {
        await NavigateAsync("/");

        // The ⚙ gear button opens the connector configuration dialog.
        var gear = Page.Locator("button[title='Connector configuration'], button[aria-label*='connector' i]").First;
        await gear.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    [Fact]
    public async Task GearIcon_Click_OpensConnectorModal()
    {
        await NavigateAsync("/");

        var gear = Page.Locator("button[title='Connector configuration'], button[aria-label*='connector' i]").First;
        await gear.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await gear.ClickAsync();

        // ConnectorConfigModal.razor renders a fixed-position overlay with an h2 header.
        var modal = Page.Locator("h2:has-text('Connector Configuration'), div.fixed.inset-0.z-50").First;
        await modal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    [Fact]
    public async Task NewTicketButton_IsVisible_OnThreadsPage()
    {
        await NavigateAsync("/");

        // The "+ New Thread" link/button must be present so users can submit tickets.
        var newBtn = Page.Locator("a:has-text('New Thread'), button:has-text('New Thread')").First;
        await newBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }
}
