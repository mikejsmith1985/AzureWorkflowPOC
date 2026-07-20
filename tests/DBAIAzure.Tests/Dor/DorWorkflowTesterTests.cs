// Unit tests for the DoR workflow health check (spec-021 T064): unconfigured, invalid config, and healthy.
using DBAIAzure.Connectors;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Core.Models.DorWorkflow.Config;
using Xunit;

namespace DBAIAzure.Tests.Dor;

public sealed class DorWorkflowTesterTests
{
    [Fact]
    public async Task Unconfigured_Fails()
    {
        var tester = new DorWorkflowTester(new StubResolver(DorWorkflowConfig.Unconfigured), new StubDoc("DoR"));

        var result = await tester.TestConnectionAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("not configured", result.Message);
    }

    [Fact]
    public async Task InvalidConfig_FailsWithIssues()
    {
        var config = new DorWorkflowConfig
        {
            IsConfigured = true,
            Jira = new DorJiraConfig { ProjectKeys = Array.Empty<string>(), ReadyTransitionId = "" },   // missing both
            Dor = new DorDocConfig { SourceType = "url", SourceUri = "https://x" },
        };
        var tester = new DorWorkflowTester(new StubResolver(config), new StubDoc("DoR"));

        var result = await tester.TestConnectionAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("ready_transition_id", result.Message);
    }

    [Fact]
    public async Task Valid_WithLoadableDocument_Passes()
    {
        var config = new DorWorkflowConfig
        {
            IsConfigured = true,
            Jira = new DorJiraConfig { ProjectKeys = new[] { "SBRO" }, ReadyTransitionId = "31" },
            Dor = new DorDocConfig { SourceType = "inline", InlineMarkdown = "# DoR" },
        };
        var tester = new DorWorkflowTester(new StubResolver(config), new StubDoc("# DoR"));

        var result = await tester.TestConnectionAsync();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DocumentLoadFailure_Fails()
    {
        var config = new DorWorkflowConfig
        {
            IsConfigured = true,
            Jira = new DorJiraConfig { ProjectKeys = new[] { "SBRO" }, ReadyTransitionId = "31" },
            Dor = new DorDocConfig { SourceType = "url", SourceUri = "https://x" },
        };
        var tester = new DorWorkflowTester(new StubResolver(config), new ThrowingDoc());

        var result = await tester.TestConnectionAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("could not be loaded", result.Message);
    }

    private sealed class StubResolver : IDorConfigResolver
    {
        private readonly DorWorkflowConfig _config;
        public StubResolver(DorWorkflowConfig config) => _config = config;
        public Task<DorWorkflowConfig> ResolveActiveAsync(CancellationToken ct = default) => Task.FromResult(_config);
        public Task<DorWorkflowSecrets> ResolveSecretsAsync(CancellationToken ct = default) =>
            Task.FromResult(new DorWorkflowSecrets(null, null, null, null));
    }

    private sealed class StubDoc : IDorDocumentSource
    {
        private readonly string _text;
        public StubDoc(string text) => _text = text;
        public Task<DorDocument> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult(new DorDocument(_text, "v1", DateTimeOffset.UtcNow, "inline"));
    }

    private sealed class ThrowingDoc : IDorDocumentSource
    {
        public Task<DorDocument> LoadAsync(CancellationToken ct = default) =>
            throw new DorDocumentUnavailableException("unreachable");
    }
}
