// E2E coverage for folding the Graph into the Workflow Builder (feature 014, US2): the standalone
// Graph destination is gone and the old /graph route redirects to the Builder. Run via run-e2e.ps1.
using DBAIAzure.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace DBAIAzure.E2ETests.Tests;

/// <summary>
/// Verifies there is no standalone Graph destination, and that the previous <c>/graph</c> route
/// resolves to the Workflow Builder (no dead end / 404) — SC-002, C-NAV-3.
/// </summary>
public sealed class GraphRedirectTests : E2ETestBase
{
    public GraphRedirectTests(WebAppFixture webApp, PlaywrightFixture playwright)
        : base(webApp, playwright) { }

    [Fact]
    public async Task Sidebar_HasNoGraphDestination()
    {
        await NavigateAsync("/");

        Assert.Equal(0, await Page.Locator("[data-testid='nav-graph']").CountAsync());
        Assert.True(await IsBlazorConnectedAsync());
    }

    [Fact]
    public async Task OldGraphRoute_RedirectsToWorkflowBuilder()
    {
        await NavigateAsync("/graph");
        await Page.WaitForURLAsync("**/workflow-builder");

        Assert.Contains("/workflow-builder", Page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.True(await IsBlazorConnectedAsync());
    }
}
