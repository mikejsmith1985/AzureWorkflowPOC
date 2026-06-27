// E2E coverage for the in-app User Guide (feature 014, US6): reachable from the sidebar, and it
// documents every primary section plus that section's key tasks (SC-009). Run via scripts/run-e2e.ps1.
using DBAIAzure.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace DBAIAzure.E2ETests.Tests;

/// <summary>
/// Verifies the User Guide destination is reachable from the sidebar and documents 100% of the
/// primary sections and their key tasks, checked against the navigation inventory (FR-016, SC-009).
/// </summary>
public sealed class UserGuideTests : E2ETestBase
{
    public UserGuideTests(WebAppFixture webApp, PlaywrightFixture playwright)
        : base(webApp, playwright) { }

    // The five primary sections from the navigation model that the guide MUST document.
    private static readonly string[] PrimarySectionKeys =
        { "monitor", "review", "automation", "configuration", "repos" };

    [Fact]
    public async Task UserGuide_IsReachable_FromSidebar()
    {
        await NavigateAsync("/");

        await Page.Locator("[data-testid='nav-guide']").ClickAsync();
        await Page.WaitForURLAsync("**/user-guide");

        await Assertions.Expect(Page.Locator("[data-testid='guide-page']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("[data-testid='guide-intro']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task UserGuide_Documents_EveryPrimarySection()
    {
        await NavigateAsync("/user-guide");

        foreach (var sectionKey in PrimarySectionKeys)
        {
            await Assertions.Expect(Page.Locator($"[data-testid='guide-section-{sectionKey}']")).ToBeVisibleAsync();
        }
    }

    [Fact]
    public async Task UserGuide_Documents_KeyTasks_PerSection()
    {
        await NavigateAsync("/user-guide");

        foreach (var sectionKey in PrimarySectionKeys)
        {
            // Each section block lists how to perform its key tasks (no major capability undocumented).
            await Assertions.Expect(Page.Locator($"[data-testid='guide-tasks-{sectionKey}']")).ToBeVisibleAsync();
        }
    }
}
