// E2E tests for the LLM key-entry interactive element (D1, Constitution Article V). The demo's whole
// premise is "the only thing a visitor supplies is their LLM API key" (FR-002/FR-003), so the LLM
// connector card's key field is a key interactive element and must have a Playwright test. These
// tests drive the UI surface only — they never make a live LLM call, so they need no real key.
using DBAIAzure.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace DBAIAzure.E2ETests.Tests;

/// <summary>
/// Validates that a visitor can find and use the LLM key-entry surface on /settings/connectors:
/// the "LLM Provider" card exposes a provider dropdown and a masked API Key field on Edit, and a
/// supplied key saves without error. Proves the FR-003 interactive element exists (Article V); it
/// does not assert a live inference (that needs a real key and is covered by the quickstart).
/// </summary>
public sealed class LlmKeyEntryTests : E2ETestBase
{
    public LlmKeyEntryTests(WebAppFixture webApp, PlaywrightFixture playwright)
        : base(webApp, playwright) { }

    private ILocator LlmCard => Page.Locator("div[class*='rounded']").Filter(new() { HasText = "LLM Provider" }).First;

    [Fact]
    public async Task LlmCard_Edit_ShowsApiKeyFieldAndProviderOptions()
    {
        await NavigateAsync("/settings/connectors");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await LlmCard.Locator("button:has-text('Edit')").First.ClickAsync();

        // The masked API Key field — the one credential a visitor must supply — must be visible.
        var apiKeyField = LlmCard.Locator("input[type='password']").First;
        await apiKeyField.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });

        // The provider dropdown must offer Anthropic (Claude).
        var providerSelect = LlmCard.Locator("select").First;
        await providerSelect.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        var optionTexts = await providerSelect.Locator("option").AllInnerTextsAsync();
        Assert.Contains(optionTexts, text => text.Contains("Anthropic"));

        Assert.True(await IsBlazorConnectedAsync(), "Blazor SignalR must be connected on /settings/connectors.");
    }

    [Fact]
    public async Task LlmCard_EnterKeyAndSave_PersistsWithoutError()
    {
        await NavigateAsync("/settings/connectors");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await LlmCard.Locator("button:has-text('Edit')").First.ClickAsync();

        // Choose a provider, type a model (no live "Fetch Models" call), and enter a dummy key.
        await LlmCard.Locator("select").First.SelectOptionAsync(new SelectOptionValue { Value = "anthropic" });
        await LlmCard.Locator("input[type='text']").First.FillAsync("claude-sonnet-4-6");
        await LlmCard.Locator("input[type='password']").First.FillAsync("sk-ant-e2e-dummy-not-a-real-key");

        await LlmCard.Locator("button:has-text('Save')").First.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Saving persists the key (no live call) and collapses the edit form — and never errors.
        var bodyText = await Page.InnerTextAsync("body");
        Assert.DoesNotContain("An unhandled error has occurred", bodyText, StringComparison.OrdinalIgnoreCase);
        await LlmCard.Locator("button:has-text('Edit')").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
    }
}
