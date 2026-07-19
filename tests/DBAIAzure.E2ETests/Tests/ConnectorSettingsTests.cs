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

        // Each connector type (AzureDevOps, LLM, ServiceNow, Messaging) should have an Edit button.
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
    public async Task MessagingCard_Edit_ShowsPlatformDropdownAndWebhookField()
    {
        await NavigateAsync("/settings/connectors");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Open the Messaging connector card's edit form.
        // Connector cards use the semantic surface token after the spec-014 restyle (was bg-gray-900).
        var messagingCard = Page.Locator("div.rounded.bg-surface", new() { HasTextString = "Messaging" }).First;
        await messagingCard.Locator("button:has-text('Edit')").First.ClickAsync();

        // The platform dropdown must offer Teams, Slack, and Discord.
        var platformSelect = messagingCard.Locator("select").First;
        await platformSelect.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        var optionTexts = await platformSelect.Locator("option").AllInnerTextsAsync();
        Assert.Contains(optionTexts, text => text.Contains("Microsoft Teams"));
        Assert.Contains(optionTexts, text => text.Contains("Slack"));
        Assert.Contains(optionTexts, text => text.Contains("Discord"));

        // A masked Webhook URL field must be present for the webhook fallback path.
        var webhookField = messagingCard.Locator("input[type='password']").First;
        await webhookField.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });

        // Switching platform (twice) must not throw / break the form (Save remains available).
        await platformSelect.SelectOptionAsync(new SelectOptionValue { Value = "Slack" });
        await platformSelect.SelectOptionAsync(new SelectOptionValue { Value = "Discord" });
        await messagingCard.Locator("button:has-text('Save')").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
    }

    [Fact]
    public async Task ConnectorSettings_NavigationFromMainNav_ReachesPage()
    {
        await NavigateAsync("/");

        // Connector settings live under the "Configuration" sidebar section (grouped IA, spec-014).
        await Page.Locator("[data-testid='nav-configuration']").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var heading = Page.Locator("h1", new() { HasTextString = "Connector Settings" });
        await heading.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });

        Assert.True(await IsBlazorConnectedAsync());
    }

    /// <summary>
    /// spec-020: the generic Work Tracking System card exposes a provider selector that switches the
    /// connection sub-form between Azure DevOps and Jira fields without a page reload.
    /// </summary>
    [Fact]
    public async Task WorkTrackerCard_ProviderSelector_SwitchesSubForms()
    {
        await NavigateAsync("/settings/connectors");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var card = WorkTrackerCard();
        await card.Locator("button:has-text('Edit')").First.ClickAsync();

        // The provider selector offers both Azure DevOps and Jira.
        var providerSelect = card.Locator("[data-testid='worktracker-provider']");
        await providerSelect.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        var optionTexts = await providerSelect.Locator("option").AllInnerTextsAsync();
        Assert.Contains(optionTexts, text => text.Contains("Azure DevOps"));
        Assert.Contains(optionTexts, text => text.Contains("Jira"));

        // Selecting Jira reveals the Jira connection fields.
        await providerSelect.SelectOptionAsync(new SelectOptionValue { Value = "Jira" });
        var jiraText = await card.InnerTextAsync();
        Assert.Contains("Site URL", jiraText);
        Assert.Contains("Project Key", jiraText);

        // Selecting Azure DevOps reveals the ADO connection fields.
        await providerSelect.SelectOptionAsync(new SelectOptionValue { Value = "AzureDevOps" });
        Assert.Contains("Organization URL", await card.InnerTextAsync());

        var bodyText = await Page.InnerTextAsync("body");
        Assert.DoesNotContain("An unhandled error has occurred", bodyText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The ADO preflight "Test Connection" button appears on the Work Tracking System card once the
    /// connector is saved with the Azure DevOps provider (spec-020 — the button is provider-specific).
    /// </summary>
    [Fact]
    public async Task WorkTrackerCard_AdoProvider_ShowsPreflightButton()
    {
        await NavigateAsync("/settings/connectors");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Configure the connector as Azure DevOps (non-secret fields are enough to persist a provider).
        var card = WorkTrackerCard();
        await card.Locator("button:has-text('Edit')").First.ClickAsync();
        await card.Locator("[data-testid='worktracker-provider']")
            .SelectOptionAsync(new SelectOptionValue { Value = "AzureDevOps" });
        await card.Locator("input[type='url']").FillAsync("https://dev.azure.com/e2e-org");
        await card.Locator("input[type='text']").First.FillAsync("E2EProject");
        await card.Locator("button:has-text('Save')").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The ADO-specific preflight button now renders on the card.
        var preflightButton = Page.Locator("[data-testid='ado-preflight-button']");
        await preflightButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });

        var bodyText = await Page.InnerTextAsync("body");
        Assert.DoesNotContain("An unhandled error has occurred", bodyText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The generic Work Tracking System connector card, located by its heading text.</summary>
    private ILocator WorkTrackerCard() =>
        Page.Locator("div.rounded.bg-surface", new() { HasTextString = "Work Tracking System" }).First;
}
