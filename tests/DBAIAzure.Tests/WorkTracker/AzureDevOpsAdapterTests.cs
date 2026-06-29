// Contract behaviours for the ADO work-tracker adapter (spec-018 C3) — exercised via FakeBoardsClient.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Core.Models.WorkTracker;
using DBAIAzure.Tests.Fakes;
using DBAIAzure.Web.Integrations.AzureDevOps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.WorkTracker;

public sealed class AzureDevOpsAdapterTests
{
    private static AzureDevOpsWorkTrackerAdapter Build(FakeBoardsClient boards, int? resolveTo = null)
    {
        // A real scope factory so the adapter can resolve the scoped preflight (provisioning path only).
        var scopeFactory = new ServiceCollection()
            .AddScoped<IAdoTelemetryPreflightService>(_ => new StubPreflight())
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        return new AzureDevOpsWorkTrackerAdapter(boards, new StubBindingMap(resolveTo), scopeFactory,
            new AdoFieldReferenceResolver(), NullLogger<AzureDevOpsWorkTrackerAdapter>.Instance);
    }

    [Fact]
    public async Task Create_MapsLogicalType_AndReturnsNumericRef()
    {
        var boards = new FakeBoardsClient();
        var adapter = Build(boards);

        var reference = await adapter.CreateWorkItemAsync(
            WorkItemType.UserStory, "title", "desc", parent: WorkItemRef.From(10));

        Assert.Equal("User Story", boards.Creates.Single().Type);
        Assert.Equal(10, boards.Creates.Single().ParentId);
        Assert.True(reference.TryAsInt(out _));   // ADO refs are numeric
    }

    [Fact]
    public async Task SetFields_ResolvesLogicalNamesToCustomReferences()
    {
        var boards = new FakeBoardsClient();
        var adapter = Build(boards);

        await adapter.SetFieldsAsync(WorkItemRef.From(42), new Dictionary<string, object?>
        {
            [LogicalField.AIRuntimeCostUSD] = 1.50,
            [LogicalField.CostBindingKey] = "BIND-X",
        });

        var fields = boards.FieldUpdates.Single().Fields;
        Assert.Equal(1.50, fields["Custom.AIRuntimeCostUSD"]);
        Assert.Equal("BIND-X", fields["Custom.CostBindingKey"]);
    }

    [Fact]
    public async Task ResolveByBindingKey_WrapsMapId_OrNullWhenUnknown()
    {
        Assert.Equal("100", (await Build(new FakeBoardsClient(), resolveTo: 100)
            .ResolveByBindingKeyAsync("BIND-X"))!.Value.Value);

        Assert.Null(await Build(new FakeBoardsClient(), resolveTo: null).ResolveByBindingKeyAsync("BIND-NONE"));
    }

    [Fact]
    public void RollupCapability_IsNativeAnalytics()
    {
        var capability = Build(new FakeBoardsClient()).GetRollupCapability();
        Assert.Equal(RollupKind.Native, capability.Kind);
        Assert.Equal("ADO Analytics", capability.NativeTool);
    }

    // ── Stubs ───────────────────────────────────────────────────────────────
    private sealed class StubBindingMap : IBindingWorkItemMap
    {
        private readonly int? _resolveTo;
        public StubBindingMap(int? resolveTo) => _resolveTo = resolveTo;
        public Task PutAsync(string bindingKey, WorkItemRef workItem, CancellationToken ct = default) => Task.CompletedTask;
        public Task<WorkItemRef?> ResolveAsync(string bindingKey, CancellationToken ct = default) =>
            Task.FromResult(_resolveTo is int id ? (WorkItemRef?)WorkItemRef.From(id) : null);
    }

    private sealed class StubPreflight : IAdoTelemetryPreflightService
    {
        public Task<PreflightResult> RunPreflightAsync(AdoTelemetryFieldConfig? overrideConfig, CancellationToken ct = default)
            => Task.FromResult(PreflightResult.Fail("not exercised in this test"));
    }
}
