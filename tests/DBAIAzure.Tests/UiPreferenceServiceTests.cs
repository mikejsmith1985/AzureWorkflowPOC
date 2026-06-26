// Unit tests for the shell presentation-preferences service: defaults, persistence round-trip via
// localStorage interop, text-scale mapping, and graceful fallback when storage is unavailable.
using DBAIAzure.Web.Services;
using Microsoft.JSInterop;
using Xunit;

namespace DBAIAzure.Tests;

public sealed class UiPreferenceServiceTests
{
    [Fact]
    public void Defaults_BeforeInitialise_AreNormalAndAssistantOpen()
    {
        var service = new UiPreferenceService(new FakeJsRuntime());

        Assert.Equal(TextSize.Normal, service.TextSize);
        Assert.True(service.IsAssistantOpen);
        Assert.Equal(1.0, service.TextScale);
    }

    [Theory]
    [InlineData(TextSize.Small, 0.9)]
    [InlineData(TextSize.Normal, 1.0)]
    [InlineData(TextSize.Large, 1.15)]
    public async Task TextScale_MapsEachSize(TextSize size, double expectedScale)
    {
        var service = new UiPreferenceService(new FakeJsRuntime());

        await service.SetTextSizeAsync(size);

        Assert.Equal(expectedScale, service.TextScale);
    }

    [Fact]
    public async Task InitialiseAsync_RestoresStoredPreferences()
    {
        var storage = new FakeJsRuntime();
        storage.Store["ui.textSize"] = "Large";
        storage.Store["ui.assistantOpen"] = "false";
        var service = new UiPreferenceService(storage);

        await service.InitialiseAsync();

        Assert.Equal(TextSize.Large, service.TextSize);
        Assert.False(service.IsAssistantOpen);
    }

    [Fact]
    public async Task SetTextSizeAsync_PersistsValueAndRaisesChange()
    {
        var storage = new FakeJsRuntime();
        var service = new UiPreferenceService(storage);
        var changed = false;
        service.OnChange += () => changed = true;

        await service.SetTextSizeAsync(TextSize.Small);

        Assert.Equal("Small", storage.Store["ui.textSize"]);
        Assert.True(changed);
    }

    [Fact]
    public async Task SetAssistantOpenAsync_PersistsValue()
    {
        var storage = new FakeJsRuntime();
        var service = new UiPreferenceService(storage);

        await service.SetAssistantOpenAsync(false);

        Assert.Equal("false", storage.Store["ui.assistantOpen"]);
        Assert.False(service.IsAssistantOpen);
    }

    [Fact]
    public async Task InitialiseAsync_WhenStorageThrows_KeepsDefaults()
    {
        var service = new UiPreferenceService(new ThrowingJsRuntime());

        await service.InitialiseAsync();

        Assert.Equal(TextSize.Normal, service.TextSize);
        Assert.True(service.IsAssistantOpen);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>In-memory IJSRuntime backing localStorageGet/Set with a dictionary.</summary>
    private sealed class FakeJsRuntime : IJSRuntime
    {
        public Dictionary<string, string> Store { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "localStorageGet")
            {
                var key = (string)args![0]!;
                object? value = Store.TryGetValue(key, out var stored) ? stored : null;
                return new ValueTask<TValue>((TValue)value!);
            }

            if (identifier == "localStorageSet")
            {
                Store[(string)args![0]!] = (string)args[1]!;
            }

            // localStorageSet (InvokeVoidAsync) lands here with TValue = IJSVoidResult.
            return new ValueTask<TValue>(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    /// <summary>Throws on every call — exercises the storage-unavailable fallback to defaults.</summary>
    private sealed class ThrowingJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException("storage unavailable");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new InvalidOperationException("storage unavailable");
    }
}
