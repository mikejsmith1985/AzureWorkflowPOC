// Unit tests for TeamsWorkflowApprovalNotifier: Graph API card dispatch (T033) and
// escalation-chain-exhausted guard (T034).
using DBAIAzure.Web.Integrations.Teams;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Unit tests for <see cref="TeamsWorkflowApprovalNotifier"/>.
/// HTTP calls are intercepted by a hand-rolled fake handler so no real network traffic occurs.
/// </summary>
public sealed class TeamsApprovalNotifierTests
{
    // ── T034: EscalateAsync throws when escalation chain is exhausted ────────

    [Fact]
    public async Task EscalateAsync_WhenChainExhausted_ThrowsInvalidOperationException()
    {
        var approvers = new[] { "approver@example.com" };
        var notifier  = BuildNotifier(statusCode: HttpStatusCode.OK);

        // currentApproverIndex 0 is already the last (count = 1), so nextIndex = 1 >= 1 → exhausted.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            notifier.EscalateAsync(
                runId:                "run-1",
                workflowName:         "Test Workflow",
                nodeLabel:            "Approval Gate",
                question:             "Approve this change?",
                approverChain:        approvers,
                currentApproverIndex: 0,
                decisionOptions:      ["Approve", "Reject"]));
    }

    [Fact]
    public async Task EscalateAsync_WhenChainHasNextApprover_DoesNotThrow()
    {
        var approvers = new[] { "approver1@example.com", "approver2@example.com" };
        var handler   = new FakeHttpHandler(HttpStatusCode.OK);
        var notifier  = BuildNotifier(handler: handler);

        // index 0 → nextIndex 1, which exists: should not throw.
        await notifier.EscalateAsync(
            runId:                "run-2",
            workflowName:         "Test Workflow",
            nodeLabel:            "Approval Gate",
            question:             "Approve this change?",
            approverChain:        approvers,
            currentApproverIndex: 0,
            decisionOptions:      ["Approve", "Reject"]);

        // At least two HTTP calls expected: one token acquisition + one card send.
        Assert.True(handler.CallCount >= 2, $"Expected ≥2 HTTP calls, got {handler.CallCount}");
    }

    // ── T033: NotifyAsync posts an Adaptive Card via Graph API ──────────────

    [Fact]
    public async Task NotifyAsync_WithApprovers_MakesHttpCallsToGraphApi()
    {
        var handler  = new FakeHttpHandler(HttpStatusCode.OK);
        var notifier = BuildNotifier(handler: handler);

        await notifier.NotifyAsync(
            runId:           "run-3",
            workflowName:    "Ticket Triage",
            nodeLabel:       "Manager Approval",
            question:        "Should this ticket be escalated?",
            approverChain:   ["manager@example.com"],
            decisionOptions: ["Approve", "Reject"]);

        // Expects at least: 1 token call + 1 card-send call to Graph API.
        Assert.True(handler.CallCount >= 2, $"Expected ≥2 HTTP calls, got {handler.CallCount}");
    }

    [Fact]
    public async Task NotifyAsync_WhenApproverListEmpty_SkipsHttpCallsSilently()
    {
        var handler  = new FakeHttpHandler(HttpStatusCode.OK);
        var notifier = BuildNotifier(handler: handler);

        // No approvers configured — implementation logs a warning and returns without calling HTTP.
        await notifier.NotifyAsync(
            runId:           "run-4",
            workflowName:    "Ticket Triage",
            nodeLabel:       "Manager Approval",
            question:        "Should this proceed?",
            approverChain:   [],
            decisionOptions: ["Approve"]);

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task NotifyAsync_WhenGraphReturnsError_DoesNotThrow()
    {
        // Non-fatal: a failing Graph call must not propagate to the run.
        var notifier = BuildNotifier(statusCode: HttpStatusCode.InternalServerError);

        await notifier.NotifyAsync(
            runId:           "run-5",
            workflowName:    "Test Workflow",
            nodeLabel:       "Approval Gate",
            question:        "Proceed?",
            approverChain:   ["someone@example.com"],
            decisionOptions: ["Yes"]);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static TeamsWorkflowApprovalNotifier BuildNotifier(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        FakeHttpHandler? handler = null)
    {
        handler ??= new FakeHttpHandler(statusCode);
        var factory = new FakeHttpClientFactory(handler);

        // Minimal config with valid tenant/client so the token-acquisition path runs.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Teams:GraphTenantId"]     = "tenant-id",
                ["Teams:GraphClientId"]     = "client-id",
                ["Teams:GraphClientSecret"] = "secret",
                ["Portal:BaseUrl"]          = "http://localhost:5000",
            })
            .Build();

        return new TeamsWorkflowApprovalNotifier(factory, config, NullLogger<TeamsWorkflowApprovalNotifier>.Instance);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        public int CallCount { get; private set; }

        public FakeHttpHandler(HttpStatusCode statusCode) => _statusCode = statusCode;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;

            // Token endpoint returns a minimal valid token response.
            if (request.RequestUri?.AbsoluteUri.Contains("oauth2/v2.0/token") == true)
            {
                var tokenJson = """{"access_token":"fake-token","expires_in":3600}""";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(tokenJson, System.Text.Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly FakeHttpHandler _handler;

        public FakeHttpClientFactory(FakeHttpHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler);
    }
}
