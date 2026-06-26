// Client-persisted presentation preferences for the Admin Console shell: the chosen text size and
// whether the right-hand Assistant panel is open. Values live only in the browser's localStorage
// (via the existing localStorageGet/Set interop) — there is no server-side store and no user account
// (spec Out of Scope). Reads happen once on the first interactive render; writes happen on change.
using Microsoft.JSInterop;

namespace DBAIAzure.Web.Services;

/// <summary>The relative text size a visitor can choose from the top bar, for readability.</summary>
public enum TextSize
{
    /// <summary>Smaller than default.</summary>
    Small,

    /// <summary>The default size.</summary>
    Normal,

    /// <summary>Larger than default.</summary>
    Large,
}

/// <summary>
/// Holds and persists the visitor's shell presentation choices (text size, Assistant panel
/// open/closed). Defaults apply until <see cref="InitialiseAsync"/> reads any stored values.
/// </summary>
public interface IUiPreferenceService
{
    /// <summary>The current text size; defaults to <see cref="TextSize.Normal"/>.</summary>
    TextSize TextSize { get; }

    /// <summary>Whether the Assistant panel is open; defaults to open.</summary>
    bool IsAssistantOpen { get; }

    /// <summary>The CSS multiplier for <c>--text-scale</c> corresponding to the current text size.</summary>
    double TextScale { get; }

    /// <summary>Raised whenever a preference changes, so the shell can re-render.</summary>
    event Action? OnChange;

    /// <summary>Reads any persisted preferences from localStorage; safe to call after first render.</summary>
    Task InitialiseAsync();

    /// <summary>Sets and persists the text size.</summary>
    Task SetTextSizeAsync(TextSize textSize);

    /// <summary>Sets and persists the Assistant panel open/closed state.</summary>
    Task SetAssistantOpenAsync(bool isOpen);
}

/// <inheritdoc />
public sealed class UiPreferenceService : IUiPreferenceService
{
    // localStorage keys. Kept stable so a returning visitor's choices are restored.
    private const string TextSizeKey = "ui.textSize";
    private const string AssistantOpenKey = "ui.assistantOpen";

    // Text-size multipliers applied to the document root via --text-scale.
    private const double SmallScale = 0.9;
    private const double NormalScale = 1.0;
    private const double LargeScale = 1.15;

    private readonly IJSRuntime _jsRuntime;

    /// <summary>Creates the service from the JS interop runtime used to reach localStorage.</summary>
    public UiPreferenceService(IJSRuntime jsRuntime) => _jsRuntime = jsRuntime;

    /// <inheritdoc />
    public TextSize TextSize { get; private set; } = TextSize.Normal;

    /// <inheritdoc />
    public bool IsAssistantOpen { get; private set; } = true;

    /// <inheritdoc />
    public double TextScale => TextSize switch
    {
        TextSize.Small => SmallScale,
        TextSize.Large => LargeScale,
        _ => NormalScale,
    };

    /// <inheritdoc />
    public event Action? OnChange;

    /// <inheritdoc />
    public async Task InitialiseAsync()
    {
        var storedTextSize = await ReadAsync(TextSizeKey);
        if (Enum.TryParse<TextSize>(storedTextSize, ignoreCase: true, out var parsedTextSize))
        {
            TextSize = parsedTextSize;
        }

        var storedAssistantOpen = await ReadAsync(AssistantOpenKey);
        if (bool.TryParse(storedAssistantOpen, out var parsedAssistantOpen))
        {
            IsAssistantOpen = parsedAssistantOpen;
        }

        OnChange?.Invoke();
    }

    /// <inheritdoc />
    public async Task SetTextSizeAsync(TextSize textSize)
    {
        if (textSize == TextSize) return;
        TextSize = textSize;
        await WriteAsync(TextSizeKey, textSize.ToString());
        OnChange?.Invoke();
    }

    /// <inheritdoc />
    public async Task SetAssistantOpenAsync(bool isOpen)
    {
        if (isOpen == IsAssistantOpen) return;
        IsAssistantOpen = isOpen;
        await WriteAsync(AssistantOpenKey, isOpen ? "true" : "false");
        OnChange?.Invoke();
    }

    /// <summary>Reads a localStorage value; a missing or unreadable key yields null (defaults apply).</summary>
    private async Task<string?> ReadAsync(string key)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<string?>("localStorageGet", key);
        }
        catch
        {
            // Pre-render (no JS yet) or storage unavailable — fall back to the default.
            return null;
        }
    }

    /// <summary>Persists a localStorage value; a failed write is swallowed so the page never breaks.</summary>
    private async Task WriteAsync(string key, string value)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorageSet", key, value);
        }
        catch
        {
            // Storage disabled / private mode — the choice simply won't persist across reloads.
        }
    }
}
