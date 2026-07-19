// Contract behaviours for the Jira work-tracker adapter (spec-018 C5) — exercised via a fake Jira REST handler.
using System.Net;
using System.Text;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.WorkTracker;
using DBAIAzure.Web.Integrations.Jira;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.WorkTracker;

public sealed class JiraAdapterTests
{
    [Fact]
    public async Task Create_PostsIssue_AndReturnsIssueKeyRef()
    {
        var handler = new FakeJiraHandler(_ => Json("""{"id":"10001","key":"PROJ-1"}""", HttpStatusCode.Created));
        var created = await Build(handler).CreateWorkItemAsync(WorkItemType.Task, "title", "desc", parent: null);

        Assert.Equal("PROJ-1", created.WorkItemId.Value);
        Assert.False(created.WorkItemId.TryAsInt(out _));     // Jira refs are string keys
        Assert.False(created.WasUpdated);
        Assert.Contains("browse/PROJ-1", created.Url);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/rest/api/3/issue", request.Path);
        Assert.Contains("\"key\":\"PROJ\"", request.Body);     // created under the configured project
    }

    [Fact]
    public async Task SetFields_ResolvesLogicalNamesToCustomFieldIds()
    {
        var handler = new FakeJiraHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/field")
                ? Json("""[{"id":"customfield_100","name":"AIRuntimeCostUSD"}]""")
                : new HttpResponseMessage(HttpStatusCode.NoContent));

        await Build(handler).SetFieldsAsync(new WorkItemRef("PROJ-1"),
            new Dictionary<string, object?> { [LogicalField.AIRuntimeCostUSD] = 1.5 });

        var put = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Put);
        Assert.Contains("customfield_100", put.Body);
        Assert.Contains("1.5", put.Body);
    }

    [Fact]
    public void RollupCapability_RequiresAdvancedRoadmaps()
    {
        var capability = Build(new FakeJiraHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))).GetRollupCapability();
        Assert.Equal(RollupKind.RequiresAddOn, capability.Kind);
        Assert.Equal("Jira Advanced Roadmaps", capability.NativeTool);
        Assert.NotNull(capability.Notice);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static JiraWorkTrackerAdapter Build(FakeJiraHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://x.atlassian.net") };
        var connection = new JiraConnection(http, "https://x.atlassian.net", "PROJ");
        return new JiraWorkTrackerAdapter(new StubConnectionFactory(connection), new StubBindingMap(), NullLogger<JiraWorkTrackerAdapter>.Instance);
    }

    private sealed class StubConnectionFactory : IJiraConnectionFactory
    {
        private readonly JiraConnection _connection;
        public StubConnectionFactory(JiraConnection connection) => _connection = connection;
        public Task<JiraConnection> GetConnectionAsync(CancellationToken ct = default) => Task.FromResult(_connection);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FakeJiraHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = [];
        public FakeJiraHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            return _responder(request);
        }
    }

    private sealed class StubBindingMap : IBindingWorkItemMap
    {
        public Task PutAsync(string bindingKey, WorkItemRef workItem, CancellationToken ct = default) => Task.CompletedTask;
        public Task<WorkItemRef?> ResolveAsync(string bindingKey, CancellationToken ct = default) => Task.FromResult<WorkItemRef?>(null);
    }
}
