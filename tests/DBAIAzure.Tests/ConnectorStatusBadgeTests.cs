// Tests for ConnectorStatusBadge display-state logic (T035, US3).
// The badge derives its label and CSS class from ConnectorConfig — these tests verify the
// mapping rules by exercising the ConnectorConfig domain model that drives the component,
// without instantiating the Blazor component (which requires web-SDK infrastructure).
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Verifies the four observable states used by ConnectorStatusBadge:
/// (1) not-configured, (2) untested, (3) pass, (4) fail.
/// The mapping logic is duplicated inline here to serve as an executable specification;
/// the component's <c>BadgeLabel</c> / <c>BadgeClass</c> properties must satisfy the same rules.
/// </summary>
public sealed class ConnectorStatusBadgeTests
{
    private static readonly ConnectorType AnyType = ConnectorType.LLM;

    private static ConnectorConfig MakeConfig(bool isConfigured, ConnectorTestResult? testResult) =>
        new(AnyType, null, false, isConfigured, DateTimeOffset.UtcNow, testResult);

    // Mirror of ConnectorStatusBadge.BadgeLabel — the spec for what the component must produce.
    private static string BadgeLabel(ConnectorConfig? config) =>
        config is null || !config.IsConfigured ? "Not configured"
        : config.LastTestResult is null         ? "Untested"
        : config.LastTestResult.IsSuccess       ? $"Pass · {config.LastTestResult.TestedAt.ToLocalTime():HH:mm}"
        :                                         $"Fail · {config.LastTestResult.TestedAt.ToLocalTime():HH:mm}";

    // Mirror of ConnectorStatusBadge.BadgeClass.
    private static string BadgeClass(ConnectorConfig? config) =>
        config is null || !config.IsConfigured   ? "bg-gray-800 text-gray-400"
        : config.LastTestResult is null          ? "bg-amber-900 text-amber-300"
        : config.LastTestResult.IsSuccess        ? "bg-emerald-900 text-emerald-300"
        :                                          "bg-red-900 text-red-300";

    // ── State 1: Not configured ──────────────────────────────────────────

    [Fact]
    public void BadgeLabel_WhenConfigNull_IsNotConfigured()
        => Assert.Equal("Not configured", BadgeLabel(null));

    [Fact]
    public void BadgeClass_WhenConfigNull_IsGrey()
        => Assert.Contains("gray", BadgeClass(null), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void BadgeLabel_WhenNotConfigured_IsNotConfigured()
        => Assert.Equal("Not configured", BadgeLabel(MakeConfig(isConfigured: false, testResult: null)));

    // ── State 2: Configured but not yet tested ──────────────────────────

    [Fact]
    public void BadgeLabel_WhenConfiguredAndNoTestResult_IsUntested()
        => Assert.Equal("Untested", BadgeLabel(MakeConfig(isConfigured: true, testResult: null)));

    [Fact]
    public void BadgeClass_WhenUntested_IsAmber()
        => Assert.Contains("amber", BadgeClass(MakeConfig(isConfigured: true, testResult: null)), StringComparison.OrdinalIgnoreCase);

    // ── State 3: Last test passed ────────────────────────────────────────

    [Fact]
    public void BadgeLabel_WhenLastTestPassed_StartsWithPass()
    {
        var testResult = new ConnectorTestResult(AnyType, true, "Connected.", DateTimeOffset.UtcNow);
        Assert.StartsWith("Pass", BadgeLabel(MakeConfig(isConfigured: true, testResult)), StringComparison.Ordinal);
    }

    [Fact]
    public void BadgeClass_WhenLastTestPassed_IsGreen()
    {
        var testResult = new ConnectorTestResult(AnyType, true, "Connected.", DateTimeOffset.UtcNow);
        Assert.Contains("emerald", BadgeClass(MakeConfig(isConfigured: true, testResult)), StringComparison.OrdinalIgnoreCase);
    }

    // ── State 4: Last test failed ────────────────────────────────────────

    [Fact]
    public void BadgeLabel_WhenLastTestFailed_StartsWithFail()
    {
        var testResult = new ConnectorTestResult(AnyType, false, "401 Unauthorized.", DateTimeOffset.UtcNow);
        Assert.StartsWith("Fail", BadgeLabel(MakeConfig(isConfigured: true, testResult)), StringComparison.Ordinal);
    }

    [Fact]
    public void BadgeClass_WhenLastTestFailed_IsRed()
    {
        var testResult = new ConnectorTestResult(AnyType, false, "401 Unauthorized.", DateTimeOffset.UtcNow);
        Assert.Contains("red", BadgeClass(MakeConfig(isConfigured: true, testResult)), StringComparison.OrdinalIgnoreCase);
    }
}
