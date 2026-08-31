// E2E tests for the LLM key-entry interactive element (D1, Constitution Article V). The demo's whole
// premise is "the only thing a visitor supplies is their LLM API key" (FR-002/FR-003), so the LLM
// connector card's key field is a key interactive element and must have a Playwright test. These
// tests drive the UI surface only — they never make a live LLM call, so they need no real key.
//
// They DO write to the one LLM connector row the whole app reads, though, and every E2E class shares a
// single app and database. Saving a dummy key here therefore used to poison every later test that needs a
// working model — NodeRealizationTests among them, whose proposals all failed 401 and left "Accept all"
// permanently disabled. The save test now restores the key it overwrote.
using System.Text.Json;
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

        // Put the working key back. The LLM connector row is shared by every test in the collection, so
        // leaving the dummy in place breaks any later test that needs a real model call.
        await RestoreWorkingKeyAsync();
    }

    /// <summary>
    /// Re-saves the developer's real API key over the dummy this class just stored, so the shared LLM
    /// connector is left as it was found. The key comes from the same two places the app itself reads —
    /// the <c>Anthropic__ApiKey</c> environment variable, else <c>appsettings.Development.json</c>, which
    /// the fixture already runs the app against. When neither holds a usable key the dummy stays; tests
    /// that need a live model are documented as requiring one, and that is the state they would hit anyway.
    /// </summary>
    private async Task RestoreWorkingKeyAsync()
    {
        var workingKey = ReadConfiguredApiKey();
        if (string.IsNullOrWhiteSpace(workingKey))
            return;

        await LlmCard.Locator("button:has-text('Edit')").First.ClickAsync();
        await LlmCard.Locator("input[type='password']").First.FillAsync(workingKey);
        await LlmCard.Locator("button:has-text('Save')").First.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await LlmCard.Locator("button:has-text('Edit')").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
    }

    /// <summary>
    /// The API key the app would use: the environment variable first, then the Development settings file.
    /// Returns null when neither holds a real key, so a placeholder is never saved over a dummy.
    /// </summary>
    private static string? ReadConfiguredApiKey()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("Anthropic__ApiKey");
        if (IsUsable(fromEnvironment))
            return fromEnvironment;

        try
        {
            var settingsPath = Path.Combine(
                WebAppFixture.RepoRoot, "src", "DBAIAzure.Web", "appsettings.Development.json");
            if (!File.Exists(settingsPath))
                return null;

            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (document.RootElement.TryGetProperty("Anthropic", out var anthropic)
                && anthropic.TryGetProperty("ApiKey", out var apiKey))
            {
                var value = apiKey.GetString();
                return IsUsable(value) ? value : null;
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            // No readable dev settings — leave the connector as-is rather than guess at a key.
        }

        return null;
    }

    /// <summary>True when the value is a real key rather than a placeholder or this class's own dummy.</summary>
    private static bool IsUsable(string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate)
        && !candidate.Contains("REPLACE", StringComparison.OrdinalIgnoreCase)
        && !candidate.Contains("dummy", StringComparison.OrdinalIgnoreCase);
}
