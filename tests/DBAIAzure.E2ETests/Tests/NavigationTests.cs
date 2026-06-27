// Validates that every navigation tab in the app loads without a Blazor error.
// These are the tests that would have caught "none of the tabs will open."
using System.Text.RegularExpressions;
using DBAIAzure.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace DBAIAzure.E2ETests.Tests;

/// <summary>
/// End-to-end tests that verify every primary destination in the Admin Console shell loads its
/// target page and that Blazor's SignalR connection is established (no reconnect overlay).
/// </summary>
public sealed class NavigationTests : E2ETestBase
{
    public NavigationTests(WebAppFixture webApp, PlaywrightFixture playwright)
        : base(webApp, playwright) { }

    [Fact]
    public async Task AppLoads_AtRoot_PageTitleContainsDBAIAzure()
    {
        await NavigateAsync("/");

        var title = await Page.TitleAsync();
        Assert.Contains("DBAIAzure", title, StringComparison.OrdinalIgnoreCase);
        Assert.True(await IsBlazorConnectedAsync(), "Blazor SignalR connection should be established.");
    }

    [Fact]
    public async Task ThreadsTab_Loads_ThreadsHeadingVisible()
    {
        await NavigateAsync("/");

        // The Threads page renders either a list of runs or an empty state — both contain
        // a heading or content div indicating the page rendered successfully.
        var header = Page.Locator("h1", new() { HasTextString = "Threads" });
        await header.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        Assert.True(await IsBlazorConnectedAsync());
    }

    [Fact]
    public async Task GraphRoute_Redirects_ToWorkflowBuilder_NoBlazorError()
    {
        // The standalone Graph page is retired (spec-014 US2); /graph now redirects to the Builder,
        // which renders the loaded workflow's own graph. The old route must never 404 or error.
        await Page.GotoAsync("/graph");

        // Client-side redirect — poll the URL instead of waiting on a "load" event that never fires.
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/workflow-builder"));
        var bodyText = await Page.InnerTextAsync("body");
        Assert.DoesNotContain("An unhandled error has occurred", bodyText, StringComparison.OrdinalIgnoreCase);
        Assert.True(await IsBlazorConnectedAsync());
    }

    [Fact]
    public async Task NewTicketTab_Loads_FormVisible()
    {
        await NavigateAsync("/new-ticket");

        // The New Ticket page renders a form with a submit button.
        var submitButton = Page.Locator("button[type='submit'], button:has-text('Submit'), button:has-text('Run')").First;
        await submitButton.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        Assert.True(await IsBlazorConnectedAsync());
    }

    [Fact]
    public async Task WorkflowBuilderTab_Loads_CanvasVisible()
    {
        // /workflow-builder/new loads the example workflow directly, bypassing the
        // first-run entry modal (which only shows when the SQLite DB is empty).
        await NavigateAsync("/workflow-builder/new");

        // Wait for the toolbar — confirms Blazor has completed its first interactive render.
        await Page.Locator(".workflow-toolbar").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });

        // Confirm the canvas container is in the DOM and has layout dimensions (height > 0).
        // Uses a JS check because Tailwind Play CDN injects CSS asynchronously via MutationObserver.
        await Page.WaitForFunctionAsync(@"
            () => {
                const el = document.getElementById('workflow-canvas-drop-zone');
                return el !== null && el.getBoundingClientRect().height > 0;
            }");

        Assert.True(await IsBlazorConnectedAsync());
    }

    [Fact]
    public async Task WorkflowGalleryPage_Loads_HeaderOrEmptyStateVisible()
    {
        await NavigateAsync("/workflow-gallery");

        // Gallery shows either a "My Workflows" heading or the empty-state message.
        var content = Page.Locator("h1:has-text('My Workflows'), :has-text('No workflows yet')").First;
        await content.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        Assert.True(await IsBlazorConnectedAsync());
    }

    [Fact]
    public async Task SidebarSections_AllPresent_AndNoGraphEntry()
    {
        await NavigateAsync("/");

        // The flat top-nav is replaced by the grouped sidebar (spec-014 US1/US3). Verify the five
        // canonical sections + the separated User Guide are present, and the retired Graph entry is not.
        foreach (var sectionKey in new[] { "monitor", "review", "automation", "configuration", "repos", "guide" })
        {
            await Assertions.Expect(Page.Locator($"[data-testid='nav-{sectionKey}']")).ToBeVisibleAsync();
        }

        Assert.Equal(0, await Page.Locator("[data-testid='nav-graph']").CountAsync());
    }

    [Fact]
    public async Task SidebarAutomation_ClickNavigatesToBuilder()
    {
        await NavigateAsync("/");

        // The Automation section's primary route is the Workflow Builder (client-side navigation).
        await Page.Locator("[data-testid='nav-automation']").ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/workflow-builder"));
        Assert.True(await IsBlazorConnectedAsync());
    }
}
