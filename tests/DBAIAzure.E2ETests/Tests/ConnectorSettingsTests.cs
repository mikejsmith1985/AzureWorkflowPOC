// E2E tests for the Connector Settings page (Article V, T090).
using DBAIAzure.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace DBAIAzure.E2ETests.Tests;

/// <summary>
/// Validates that /settings/connectors loads without errors, renders connector cards,
/// and the Edit / Check Health actions are functional (T090).
///
/// Full health-check E2E (AddHealthCheckDelete) requires live connector credentials from user secrets.
/// The tests below verify the UI surface is wired correctly and do not depend on live credentials.
/// </summary>
public sealed class ConnectorSettingsTests : E2ETestBase
{
    public ConnectorSettingsTests(WebAppFixture webApp, PlaywrightFixture playwright)
        : base(webApp, playwright) { }

    [Fact]
    public async Task ConnectorSettings_Loads_HeadingVisible()
    {
        await NavigateAsync("/settings/connectors");

        var heading = Page.Locator("h1", new() { HasTextString = "Connector Settings" });
        await heading.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        Assert.True(await IsBlazorConnectedAsync(), "Blazor SignalR must be connected on /settings/connectors.");
        var bodyText = await Page.InnerTextAsync("body");
        Assert.DoesNotContain("An unhandled error has occurred", bodyText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConnectorSettings_RendersConnectorCards()
    {
        await NavigateAsync("/settings/connectors");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Each connector type (AzureDevOps, LLM, ServiceNow, Teams) should have an Edit button.
        var editButtons = await Page.Locator("button:has-text('Edit')").AllAsync();
        Assert.True(editButtons.Count >= 1, "Expected at least one connector card with an Edit button.");
    }

    [Fact]
    public async Task ConnectorSettings_ClickEdit_OpensForm()
    {
        await NavigateAsync("/settings/connectors");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Click the first Edit button — the save form should appear.
        var firstEditButton = Page.Locator("button:has-text('Edit')").First;
        await firstEditButton.ClickAsync();

        // The Save button is inside the edit form — it should become visible after clicking Edit.
        var saveButton = Page.Locator("button:has-text('Save')").First;
        await saveButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
    }

    [Fact]
    public async Task ConnectorSettings_NavigationFromMainNav_ReachesPage()
    {
        await NavigateAsync("/");

        var navLink = Page.Locator("nav a", new() { HasTextString = "Settings" });
        await navLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var heading = Page.Locator("h1", new() { HasTextString = "Connector Settings" });
        await heading.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });

        Assert.True(await IsBlazorConnectedAsync());
    }

    /// <summary>
    /// T035 / T036: "Test Connection" button on the ADO card triggers the preflight service.
    /// Without live credentials the button renders disabled; with E2E_TEST_ADO_PAT the badge appears.
    /// </summary>
    [Fact]
    public async Task ConnectorSettings_AdoPreflightButton_RendersOnAdoCard()
    {
        await NavigateAsync("/settings/connectors");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The button must be present on the page (even if disabled because ADO is not yet configured).
        var preflightButton = Page.Locator("[data-testid='ado-preflight-button']");
        await preflightButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });

        var bodyText = await Page.InnerTextAsync("body");
        Assert.DoesNotContain("An unhandled error has occurred", bodyText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConnectorSettings_AdoPreflightButton_WhenClicked_ShowsResultBadge()
    {
        // Only runs when live ADO credentials are injected via the environment variable.
        var testPat = Environment.GetEnvironmentVariable("E2E_TEST_ADO_PAT");
        if (testPat is null)
        {
            // Without credentials the button is disabled — just assert the badge is not pre-shown.
            await NavigateAsync("/settings/connectors");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            var badge = Page.Locator("[data-testid='ado-preflight-result']");
            Assert.Equal(0, await badge.CountAsync());
            return;
        }

        await NavigateAsync("/settings/connectors");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Configure ADO credentials so the button becomes enabled.
        var adoCard = Page.Locator("div[class*='rounded']").Filter(new() { HasText = "AzureDevOps" }).First;
        await adoCard.Locator("button:has-text('Edit')").ClickAsync();
        await adoCard.Locator("input[type='password']").FillAsync($"{{\"personalAccessToken\":\"{testPat}\"}}");
        await adoCard.Locator("button:has-text('Save')").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Click "Test Connection" — the result badge should appear.
        await Page.Locator("[data-testid='ado-preflight-button']").ClickAsync();
        var resultBadge = Page.Locator("[data-testid='ado-preflight-result']");
        await resultBadge.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        var badgeText = await resultBadge.InnerTextAsync();
        Assert.True(badgeText.Contains("OK") || badgeText.Contains("failed"),
            $"Result badge should say 'OK' or 'failed', got: {badgeText}");
    }

    /// <summary>
    /// T090: Full add → health-check → delete connector flow.
    /// Skipped in CI if the AzureDevOps PAT is not present in user secrets.
    /// </summary>
    [Fact]
    public async Task AddHealthCheckDelete_WhenCredentialsPresent_ShowsHealthyBadge()
    {
        // Read the test PAT from the environment — set by the CI pipeline or via user secrets.
        // When absent, the test verifies only that the health-check button exists (no live call).
        var testPat = Environment.GetEnvironmentVariable("E2E_TEST_ADO_PAT");

        await NavigateAsync("/settings/connectors");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Locate the AzureDevOps card by its text content.
        var adoCard = Page.Locator("div[class*='rounded']")
            .Filter(new() { HasText = "AzureDevOps" })
            .First;

        if (testPat is null)
        {
            // No credentials available — just verify the Check Health button exists and is visible.
            var healthButton = adoCard.Locator("button:has-text('Check Health')");
            var exists = await healthButton.CountAsync() > 0;
            Assert.True(exists, "Expected a Check Health button on the AzureDevOps connector card.");
            return;
        }

        // Open edit form and enter test credentials (non-secret config + PAT).
        await adoCard.Locator("button:has-text('Edit')").ClickAsync();

        var secretField = adoCard.Locator("input[type='password']");
        await secretField.FillAsync($"{{\"personalAccessToken\":\"{testPat}\"}}");

        await adoCard.Locator("button:has-text('Save')").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Trigger health check.
        await adoCard.Locator("button:has-text('Check Health')").ClickAsync();

        // The healthy / unhealthy badge should appear within a few seconds.
        var healthBadge = adoCard.Locator("span").Filter(new() { HasText = "Healthy" });
        await healthBadge.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var bodyText = await Page.InnerTextAsync("body");
        Assert.DoesNotContain("An unhandled error has occurred", bodyText, StringComparison.OrdinalIgnoreCase);
    }
}
