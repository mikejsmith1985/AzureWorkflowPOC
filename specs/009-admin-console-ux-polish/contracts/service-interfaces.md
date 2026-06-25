# Service Interface Contracts: Admin Console UX Polish

## ITooltipService

**Location**: `src/DBAIAzure.Web/Services/TooltipService.cs`  
**Lifetime**: Scoped (one per Blazor circuit/session)  
**Purpose**: Portal-based tooltip state manager — consumed by `InfoTip.razor` to show/hide, and by `MainLayout.razor` to render the floating panel at layout root.

```csharp
namespace DBAIAzure.Web.Services;

public interface ITooltipService
{
    /// Current tooltip content and position; null when no tooltip is active.
    TooltipContext? ActiveTooltip { get; }

    /// Fires whenever ActiveTooltip changes — MainLayout subscribes to trigger re-render.
    event Action? OnChange;

    /// Show a tooltip anchored to the given element bounding rect.
    void Show(string content, string? example, BoundingRect anchor);

    /// Hide the active tooltip.
    void Hide();
}

/// Tooltip display data produced by InfoTip and consumed by the portal renderer.
public record TooltipContext(string Content, string? Example, BoundingRect Anchor);
```

**Behavior rules**:
- `Show` replaces any currently active tooltip (no stacking).
- `Hide` sets `ActiveTooltip` to `null` and fires `OnChange`.
- `MainLayout` calls `StateHasChanged()` in the `OnChange` handler.
- `InfoTip.razor` calls `Show` on `@onmouseenter` and `Hide` on `@onmouseleave` of the info icon.
- Tooltip flips from top to bottom (CSS class swap) when anchor top < 120px from viewport edge — computed in the portal renderer component.

---

## IOnboardingStateService

**Location**: `src/DBAIAzure.Web/Services/OnboardingStateService.cs`  
**Lifetime**: Scoped  
**Purpose**: Manages the onboarding banner visibility state — reads/writes browser localStorage and queries `IConnectorHealthChecker` for LLM health.

```csharp
namespace DBAIAzure.Web.Services;

public interface IOnboardingStateService
{
    /// Current onboarding state. Populated after InitialiseAsync completes.
    OnboardingState State { get; }

    /// Load localStorage dismissed flag and run LLM health check.
    /// Must be called from OnAfterRenderAsync(firstRender: true) in the host component.
    Task InitialiseAsync(CancellationToken ct = default);

    /// Persist dismissal to localStorage and update State.IsDismissed.
    Task DismissAsync(CancellationToken ct = default);
}
```

**Behavior rules**:
- If `IConnectorHealthChecker.TestAsync(ConnectorType.LLM)` throws, `State.IsLlmHealthy` is `false` (per clarification Q5).
- `InitialiseAsync` is idempotent — safe to call on each page render.
- `DismissAsync` writes `localStorage["onboarding_dismissed"] = "true"` via `IJSRuntime`.
- `State.ShouldShow` is `!State.IsLlmHealthy && !State.IsDismissed`.

---

## ConnectorFieldSchema (Static Factory)

**Location**: `src/DBAIAzure.Core/Models/ConnectorFieldSchema.cs`  
**Purpose**: Returns the hardcoded field descriptor list for each `ConnectorType`.

```csharp
namespace DBAIAzure.Core.Models;

public static class ConnectorFieldSchema
{
    /// Returns the ordered list of field descriptors for the given connector type.
    public static IReadOnlyList<ConnectorFieldDescriptor> For(ConnectorType type) => type switch
    {
        ConnectorType.ServiceNow   => ServiceNowFields,
        ConnectorType.AzureDevOps  => AzureDevOpsFields,
        ConnectorType.LLM          => LlmFields,
        ConnectorType.Teams        => TeamsFields,
        _                          => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    // --- ServiceNow ---
    private static readonly IReadOnlyList<ConnectorFieldDescriptor> ServiceNowFields = [
        new("InstanceUrl",  "ServiceNow URL",  ConnectorFieldType.Url,
            Placeholder:     "https://acme.service-now.com",
            TooltipContent:  "The base URL of your ServiceNow instance.",
            TooltipExample:  "https://acme.service-now.com",
            IsRequired:      true),
        new("Username",     "Username",        ConnectorFieldType.Text,
            Placeholder:     "svc-pipeline",
            TooltipContent:  "The service account username used for API authentication.",
            TooltipExample:  "svc-pipeline",
            IsRequired:      true),
        new("Password",     "Password",        ConnectorFieldType.Secret,
            Placeholder:     "",
            TooltipContent:  "The service account password or API token.",
            TooltipExample:  "",
            IsRequired:      true),
    ];

    // --- Azure DevOps ---
    private static readonly IReadOnlyList<ConnectorFieldDescriptor> AzureDevOpsFields = [
        new("OrganizationUrl",      "Organisation URL",       ConnectorFieldType.Url,
            Placeholder:             "https://dev.azure.com/my-org",
            TooltipContent:          "Your Azure DevOps organisation URL.",
            TooltipExample:          "https://dev.azure.com/my-org",
            IsRequired:              true),
        new("ProjectName",           "Project Name",           ConnectorFieldType.Text,
            Placeholder:             "MyProject",
            TooltipContent:          "The name of the target project within your organisation.",
            TooltipExample:          "MyProject",
            IsRequired:              true),
        new("PersonalAccessToken",   "Personal Access Token",  ConnectorFieldType.Secret,
            Placeholder:             "",
            TooltipContent:          "A Personal Access Token with Work Items read/write scope.",
            TooltipExample:          "",
            IsRequired:              true),
    ];

    // --- LLM ---
    private static readonly IReadOnlyList<ConnectorFieldDescriptor> LlmFields = [
        new("ProviderEndpoint",  "Provider Endpoint",  ConnectorFieldType.Url,
            Placeholder:          "https://api.anthropic.com",
            TooltipContent:       "The base URL of your LLM provider.",
            TooltipExample:       "https://api.anthropic.com",
            IsRequired:           true),
        new("ModelName",         "Model Name",         ConnectorFieldType.Text,
            Placeholder:          "claude-sonnet-4-6",
            TooltipContent:       "The model identifier sent with each inference request.",
            TooltipExample:       "claude-sonnet-4-6",
            IsRequired:           true),
        new("ApiKey",            "API Key",            ConnectorFieldType.Secret,
            Placeholder:          "",
            TooltipContent:       "Your provider API key.",
            TooltipExample:       "",
            IsRequired:           true),
    ];

    // --- Teams ---
    private static readonly IReadOnlyList<ConnectorFieldDescriptor> TeamsFields = [
        new("WebhookUrl",  "Webhook URL",  ConnectorFieldType.Secret,
            Placeholder:    "",
            TooltipContent: "The Power Automate or incoming webhook URL for your Teams channel. "
                          + "The URL contains an authentication signature — treat it as a secret.",
            TooltipExample: "https://prod-xx.logic.azure.com:443/workflows/...",
            IsRequired:     true),
    ];
}
```

---

## Component Parameter Contracts

### InfoTip.razor

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `Content` | `string` | Yes | Plain-English field description shown in tooltip body |
| `Example` | `string?` | No | Concrete example value rendered below the description |

**Behavior**: Renders an inline `ℹ` icon button. On `mouseenter`, calls `ITooltipService.Show()` with the component's bounding rect (via JS `getBoundingClientRect`). On `mouseleave`, calls `ITooltipService.Hide()`. Keyboard accessible: `aria-label="More information"`.

---

### ConnectorFieldEditor.razor

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `Descriptor` | `ConnectorFieldDescriptor` | Yes | Field metadata from `ConnectorFieldSchema.For()` |
| `Value` | `string` | Yes | Current draft value (two-way bound) |
| `ValueChanged` | `EventCallback<string>` | Yes | Fires on input change (clears parent TestResult) |
| `ValidationError` | `string?` | No | Error message displayed below the field on blur |
| `HasStoredSecret` | `bool` | No | When `true` and field type is `Secret`, renders "Key saved" badge |
| `OnRemoveSecret` | `EventCallback` | No | Fires when user clicks "Remove stored key" |

**Behavior**: Renders label + InfoTip icon + input (type driven by `Descriptor.FieldType`) + validation error. Secret fields render with reveal/hide toggle (`aria-label="Show/hide {Descriptor.Label}"`).

---

### OnboardingBanner.razor

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| *(none — reads from cascaded `IOnboardingStateService`)* | | | |

**Behavior**: Renders only when `IOnboardingStateService.State.ShouldShow == true`. Shows LLM connector as required primary step with a direct link to `/settings/connectors?expand=LLM`. Shows other three connectors as optional secondary steps. Dismiss button calls `IOnboardingStateService.DismissAsync()`.
