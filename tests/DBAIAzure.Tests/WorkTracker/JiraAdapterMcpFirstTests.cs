// Proves the Jira adapter is MCP-first: when the MCP transport can serve a read or a transition it is used and
// Jira's REST API is never called; when it cannot, the adapter falls back to REST transparently.
using System.Net;
using System.Text;
using DBAIAzure.Core.Models.WorkTracker;
using DBAIAzure.Web.Integrations.Jira;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.WorkTracker;

public sealed class JiraAdapterMcpFirstTests
{
    [Fact]
    public async Task ReadWorkItemAsync_WhenMcpCanServeIt_DoesNotTouchTheRestApi()
    {
        var handler = new CountingHandler();
        var mcp = new StubJiraMcpClient
        {
            ReadResult = new WorkItemFields(
                "SBRO-1", "https://acme.atlassian.net/browse/SBRO-1",
                new Dictionary<string, string?> { ["summary"] = "From MCP" }),
        };

        var fields = await Build(handler, mcp).ReadWorkItemAsync(new WorkItemRef("SBRO-1"), new[] { "summary" });

        Assert.Equal("From MCP", fields.Fields["summary"]);
        Assert.Equal(0, handler.RequestCount);
        Assert.Equal("SBRO-1", Assert.Single(mcp.RequestedReads));
    }

    [Fact]
    public async Task ReadWorkItemAsync_WhenMcpIsUnavailable_FallsBackToTheRestApi()
    {
        var handler = new CountingHandler(
            """{"key":"SBRO-1","fields":{"summary":"From REST"}}""");

        var fields = await Build(handler, new StubJiraMcpClient())
            .ReadWorkItemAsync(new WorkItemRef("SBRO-1"), new[] { "summary" });

        Assert.Equal("From REST", fields.Fields["summary"]);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task TransitionAsync_WhenMcpAcceptsIt_DoesNotAlsoPostToTheRestApi()
    {
        var handler = new CountingHandler();
        var mcp = new StubJiraMcpClient { CanTransition = true };

        var result = await Build(handler, mcp).TransitionAsync(new WorkItemRef("SBRO-1"), "31");

        Assert.Equal("31", result);
        Assert.Equal(0, handler.RequestCount);   // a double transition would be a real defect
        Assert.Equal("31", Assert.Single(mcp.RequestedTransitions));
    }

    [Fact]
    public async Task TransitionAsync_WhenMcpCannotDoIt_StillTransitionsOverRest()
    {
        var handler = new CountingHandler("{}");

        var result = await Build(handler, new StubJiraMcpClient()).TransitionAsync(new WorkItemRef("SBRO-1"), "31");

        Assert.Equal("31", result);
        Assert.Equal(1, handler.RequestCount);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static JiraWorkTrackerAdapter Build(CountingHandler handler, StubJiraMcpClient mcp)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://acme.atlassian.net") };
        return new JiraWorkTrackerAdapter(
            new StubFactory(new JiraConnection(http, "https://acme.atlassian.net", "SBRO")),
            mcp, new StubMap(), NullLogger<JiraWorkTrackerAdapter>.Instance);
    }

    /// <summary>Counts REST calls so a test can assert Jira's API was bypassed entirely.</summary>
    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly string _body;
        public int RequestCount { get; private set; }

        public CountingHandler(string body = "{}") => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubFactory : IJiraConnectionFactory
    {
        private readonly JiraConnection _connection;
        public StubFactory(JiraConnection connection) => _connection = connection;
        public Task<JiraConnection> GetConnectionAsync(CancellationToken ct = default) => Task.FromResult(_connection);
    }

    private sealed class StubMap : DBAIAzure.Core.Interfaces.IBindingWorkItemMap
    {
        public Task<WorkItemRef?> ResolveAsync(string bindingKey, CancellationToken ct = default) =>
            Task.FromResult<WorkItemRef?>(null);
        public Task PutAsync(string bindingKey, WorkItemRef workItem, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
