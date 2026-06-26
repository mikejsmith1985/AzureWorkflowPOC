// E2E coverage for the Admin Console shell (feature 014, US1): the persistent sidebar + top bar +
// Assistant rail, the active-section highlight, and responsive behaviour at laptop/tablet widths.
// Run via scripts/run-e2e.ps1. (Article V shippability gate / SC-001, SC-004, SC-007.)
using DBAIAzure.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace DBAIAzure.E2ETests.Tests;

/// <summary>
/// Verifies the shell frame (sidebar, top bar, Assistant rail) is present and persistent across
/// destinations, the active section is highlighted, and the sidebar stays usable from laptop down to
/// tablet widths with no horizontal scroll on the main frame.
/// </summary>
public sealed class ShellNavigationTests : E2ETestBase
{
    public ShellNavigationTests(WebAppFixture webApp, PlaywrightFixture playwright)
        : base(webApp, playwright) { }

    [Theory]
    [InlineData("/")]
    [InlineData("/apps")]
    [InlineData("/workflow-builder")]
    public async Task Shell_SidebarAndTopBar_PersistAcrossDestinations(string path)
    {
        await NavigateAsync(path);

        await Page.Locator("[data-testid='app-sidebar']").WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await Page.Locator("[data-testid='app-topbar']").WaitForAsync(new() { State = WaitForSelectorState.Visible });

        Assert.True(await IsBlazorConnectedAsync());
    }

    [Fact]
    public async Task AssistantRail_IsPresent_OnWideViewport()
    {
        await Page.SetViewportSizeAsync(1440, 900);
        await NavigateAsync("/apps");

        await Page.Locator("[data-testid='assistant-rail']").WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    [Fact]
    public async Task Sidebar_HighlightsExactlyTheActiveSection()
    {
        await NavigateAsync("/apps");

        // Repos & Apps is the active section on /apps; Monitor must not be marked current.
        await Assertions.Expect(Page.Locator("[data-testid='nav-repos'][aria-current='page']")).ToBeVisibleAsync();
        Assert.Equal(0, await Page.Locator("[data-testid='nav-monitor'][aria-current='page']").CountAsync());
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(1024)]
    [InlineData(768)]
    public async Task Shell_NoHorizontalScroll_AndDestinationsReachable(int width)
    {
        await Page.SetViewportSizeAsync(width, 900);
        await NavigateAsync("/");

        // The sidebar (full or collapsed icon rail) is present and every destination link stays reachable.
        await Page.Locator("[data-testid='app-sidebar']").WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await Assertions.Expect(Page.Locator("[data-testid='nav-repos']")).ToBeVisibleAsync();

        // No horizontal scrollbar on the main frame at any supported width (SC-007 / C-SHELL-3).
        var hasHorizontalScroll = await Page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > window.innerWidth + 1");
        Assert.False(hasHorizontalScroll, $"Unexpected horizontal scroll at {width}px width.");
    }
}
