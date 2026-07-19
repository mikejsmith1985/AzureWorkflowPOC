// Unit tests proving the active-provider routing adapter switches targets per call (spec-020, T026 core).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Core.Models.WorkTracker;
using DBAIAzure.Web.Services;
using Xunit;

namespace DBAIAzure.Tests.WorkTracker;

/// <summary>
/// Verifies that <see cref="ActiveWorkTrackerAdapter"/> resolves the active provider on each call and routes
/// to the matching adapter — so switching the provider in the connector store (as the UI does) redirects the
/// next operation without rebuilding the holder or restarting the app.
/// </summary>
public class ActiveWorkTrackerAdapterTests
{
    [Fact]
    public async Task RoutesToSelectedProvider_AndSwitchesWhenResolverChanges()
    {
        var ado = new RecordingAdapter("AzureDevOps");
        var jira = new RecordingAdapter("Jira");
        var resolver = new MutableResolver(WorkTrackerProvider.AzureDevOps);
        var routing = new ActiveWorkTrackerAdapter([ado, jira], resolver);

        await routing.AppendCommentAsync(new WorkItemRef("1"), "x");
        Assert.Equal(1, ado.Calls);
        Assert.Equal(0, jira.Calls);

        // Operator switches the active provider to Jira in the store.
        resolver.Provider = WorkTrackerProvider.Jira;
        await routing.AppendCommentAsync(new WorkItemRef("PROJ-2"), "y");
        Assert.Equal(1, ado.Calls);
        Assert.Equal(1, jira.Calls);
    }

    private sealed class MutableResolver : IWorkTrackerConfigResolver
    {
        public WorkTrackerProvider Provider { get; set; }
        public MutableResolver(WorkTrackerProvider provider) => Provider = provider;
        public Task<ResolvedWorkTrackerConfig> ResolveActiveAsync(CancellationToken ct = default) =>
            Task.FromResult(new ResolvedWorkTrackerConfig(Provider, "{}", null, IsConfigured: true));
    }

    private sealed class RecordingAdapter : IWorkTrackerAdapter
    {
        public RecordingAdapter(string key) => TrackerKey = key;
        public string TrackerKey { get; }
        public int Calls { get; private set; }

        public Task AppendCommentAsync(WorkItemRef item, string comment, CancellationToken ct = default)
        {
            Calls++;
            return Task.CompletedTask;
        }

        public Task<CreatedWorkItemRef> CreateWorkItemAsync(WorkItemType type, string title, string description, WorkItemRef? parent, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<CreatedWorkItemRef> UpsertWorkItemAsync(WorkItemRef item, string title, string description, string appendComment, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task SetFieldsAsync(WorkItemRef item, IReadOnlyDictionary<string, object?> logicalFields, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<WorkItemRef?> ResolveByBindingKeyAsync(string bindingKey, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<ProvisioningResult> ProvisionFieldsAsync(AdoTelemetryFieldConfig fieldConfig, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public RollupCapability GetRollupCapability() => throw new NotImplementedException();
    }
}
