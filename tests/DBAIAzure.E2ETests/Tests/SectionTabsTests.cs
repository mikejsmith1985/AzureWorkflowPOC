// E2E coverage for grouped sub-tabs (feature 014, US3): a multi-view section renders its sub-tabs
// with the active one highlighted, and switching tabs navigates. Run via scripts/run-e2e.ps1.
using DBAIAzure.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace DBAIAzure.E2ETests.Tests;

/// <summary>
/// Verifies the Automation section exposes Workflow Builder + Workflow Gallery as sub-tabs, the active
/// sub-tab tracks the route, and single-view sections render no sub-tab strip (FR-011, C-NAV-2).
/// </summary>
public sealed class SectionTabsTests : E2ETestBase
{
    public SectionTabsTests(WebAppFixture webApp, PlaywrightFixture playwright)
        : base(webApp, playwright) { }

    [Fact]
    public async Task Automation_ShowsSubTabs_WithActiveOnBuilder()
    {
        await NavigateAsync("/workflow-builder");

        await Page.Locator("[data-testid='section-tabs']").WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await Assertions.Expect(Page.Locator("[data-testid='subtab-workflow-builder'][aria-current='page']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("[data-testid='subtab-workflow-gallery']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task SubTab_Navigates_ToGallery()
    {
        await NavigateAsync("/workflow-builder");

        await Page.Locator("[data-testid='subtab-workflow-gallery']").ClickAsync();
        await Page.WaitForURLAsync("**/workflow-gallery");

        await Assertions.Expect(Page.Locator("[data-testid='subtab-workflow-gallery'][aria-current='page']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task SingleViewSection_RendersNoSubTabs()
    {
        // Review Queue maps to a single screen, so no sub-tab strip should appear.
        await NavigateAsync("/review-queue");

        Assert.Equal(0, await Page.Locator("[data-testid='section-tabs']").CountAsync());
        Assert.True(await IsBlazorConnectedAsync());
    }
}
