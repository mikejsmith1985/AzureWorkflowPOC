// E2E coverage for the Apps surface (feature 013): the new nav tab, register form + validation,
// the simulated executor indicator, and a sim-mode register happy path. Run via scripts/run-e2e.ps1.
using DBAIAzure.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace DBAIAzure.E2ETests.Tests;

/// <summary>
/// Verifies the Apps tab loads, the register form validates and persists, and the active-executor
/// indicator is shown — the key interactive elements of feature 013 (Article V shippability gate).
/// </summary>
public sealed class AppsPageTests : E2ETestBase
{
    public AppsPageTests(WebAppFixture webApp, PlaywrightFixture playwright)
        : base(webApp, playwright) { }

    [Fact]
    public async Task AppsTab_Loads_HeadingVisible()
    {
        await NavigateAsync("/apps");

        var heading = Page.Locator("h1", new() { HasTextString = "Monitored Apps" });
        await heading.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        Assert.True(await IsBlazorConnectedAsync());
    }

    [Fact]
    public async Task NavLink_Apps_Present_AndNavigates()
    {
        await NavigateAsync("/");

        // Apps lives under the "Repos & Apps" sidebar section (grouped IA, spec-014).
        await Page.Locator("[data-testid='nav-repos']").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        Assert.Contains("/apps", Page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.True(await IsBlazorConnectedAsync());
    }

    [Fact]
    public async Task ExecutorIndicator_IsVisible()
    {
        await NavigateAsync("/apps");

        // The indicator distinguishes Simulated vs Docker (FR-015).
        var indicator = Page.Locator("text=Executor:").First;
        await indicator.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        Assert.True(await IsBlazorConnectedAsync());
    }

    [Fact]
    public async Task Register_InvalidPath_ShowsError()
    {
        await NavigateAsync("/apps");

        await Page.Locator("button:has-text('Register App')").ClickAsync();
        await Page.Locator("input[placeholder='my-app']").FillAsync("e2e-invalid");
        await Page.GetByPlaceholder("C:\\ProjectsWin\\DBAI").FillAsync("/path/that/does/not/exist/12345");
        await Page.GetByPlaceholder("npm start").FillAsync("echo run");
        await Page.Locator("button:has-text('Register')").Last.ClickAsync();

        var error = Page.Locator("[role='alert']");
        await error.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        Assert.True(await IsBlazorConnectedAsync());
    }

    [Fact]
    public async Task Register_ValidPath_AppearsInList()
    {
        await NavigateAsync("/apps");

        // The server's content root is guaranteed to exist — a valid local repo path for the test.
        var name = "e2e-app-" + Guid.NewGuid().ToString("N")[..6];
        await Page.Locator("button:has-text('Register App')").ClickAsync();
        await Page.Locator("input[placeholder='my-app']").FillAsync(name);
        await Page.GetByPlaceholder("C:\\ProjectsWin\\DBAI").FillAsync(".");
        await Page.GetByPlaceholder("npm start").FillAsync("echo run");
        await Page.Locator("button:has-text('Register')").Last.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(Page.Locator($"text={name}")).ToBeVisibleAsync();
        Assert.True(await IsBlazorConnectedAsync());
    }
}
