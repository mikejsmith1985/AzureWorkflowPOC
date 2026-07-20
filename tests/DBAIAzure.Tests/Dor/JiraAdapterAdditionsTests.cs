// Contract tests for the spec-021 Jira adapter additions: reading a work item into a normalized field map, and
// applying a status transition. Exercised via a fake Jira REST handler (Article V: no live I/O).
using System.Net;
using System.Text;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.WorkTracker;
using DBAIAzure.Web.Integrations.Jira;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.Dor;

public sealed class JiraAdapterAdditionsTests
{
    [Fact]
    public async Task ReadWorkItem_FlattensFields_IncludingAdfDescription()
    {
        const string body = """
            {"key":"SBRO-1","fields":{
                "summary":"Add export",
                "description":{"type":"doc","version":1,"content":[
                    {"type":"paragraph","content":[{"type":"text","text":"Needs acceptance criteria"}]}]},
                "customfield_10016":5}}
            """;
        var handler = new FakeJiraHandler(_ => Json(body));

        var result = await Build(handler).ReadWorkItemAsync(
            new WorkItemRef("SBRO-1"), new[] { "summary", "description", "customfield_10016" });

        Assert.Equal("SBRO-1", result.Key);
        Assert.Contains("browse/SBRO-1", result.Url);
        Assert.Equal("Add export", result.Fields["summary"]);
        Assert.Contains("Needs acceptance criteria", result.Fields["description"]);
        Assert.Equal("5", result.Fields["customfield_10016"]);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("/rest/api/3/issue/SBRO-1", request.Path);
    }

    [Fact]
    public async Task Transition_PostsTransitionId_AndReturnsIt()
    {
        var handler = new FakeJiraHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var applied = await Build(handler).TransitionAsync(new WorkItemRef("SBRO-1"), "31");

        Assert.Equal("31", applied);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/rest/api/3/issue/SBRO-1/transitions", request.Path);
        Assert.Contains("\"id\":\"31\"", request.Body);
    }

    [Fact]
    public async Task Transition_OnFailure_ThrowsTransitionException()
    {
        var handler = new FakeJiraHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));

        await Assert.ThrowsAsync<WorkTrackerTransitionException>(
            () => Build(handler).TransitionAsync(new WorkItemRef("SBRO-1"), "999"));
    }

    // ── Helpers (mirror JiraAdapterTests) ───────────────────────────────────────

    private static JiraWorkTrackerAdapter Build(FakeJiraHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://x.atlassian.net") };
        var connection = new JiraConnection(http, "https://x.atlassian.net", "SBRO");
        return new JiraWorkTrackerAdapter(new StubConnectionFactory(connection), new StubBindingMap(), NullLogger<JiraWorkTrackerAdapter>.Instance);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubConnectionFactory : IJiraConnectionFactory
    {
        private readonly JiraConnection _connection;
        public StubConnectionFactory(JiraConnection connection) => _connection = connection;
        public Task<JiraConnection> GetConnectionAsync(CancellationToken ct = default) => Task.FromResult(_connection);
    }

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
