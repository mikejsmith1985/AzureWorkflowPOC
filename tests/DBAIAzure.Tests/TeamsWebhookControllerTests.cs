// Unit tests for TeamsWebhookController: authentication guard (T035) and approval routing (T036).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.Json;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Unit tests for <see cref="TeamsWebhookController"/> without a full ASP.NET pipeline —
/// the controller is instantiated directly so we can verify HTTP-level responses
/// using only the types that ship with ASP.NET Core.
/// </summary>
public sealed class TeamsWebhookControllerTests
{
    private const string ValidSecret = "test-secret-abc123";

    // ── T035: 401 when auth absent or wrong ──────────────────────────────────

    [Fact]
    public void ReceiveApproval_WhenSecretHeaderMissing_ReturnsUnauthorized()
    {
        var orchestrator = new RecordingOrchestrator();
        var controller   = BuildController(orchestrator, configuredSecret: ValidSecret);
        controller.ControllerContext = MakeContext(secretHeader: null);

        var body   = MakeBody("run-1", "approve");
        var result = controller.ReceiveApproval(body);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(0, orchestrator.SubmitApprovalCallCount);
    }

    [Fact]
    public void ReceiveApproval_WhenSecretHeaderWrong_ReturnsUnauthorized()
    {
        var orchestrator = new RecordingOrchestrator();
        var controller   = BuildController(orchestrator, configuredSecret: ValidSecret);
        controller.ControllerContext = MakeContext(secretHeader: "wrong-secret");

        var body   = MakeBody("run-1", "approve");
        var result = controller.ReceiveApproval(body);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(0, orchestrator.SubmitApprovalCallCount);
    }

    // ── T036: Approve/reject routing when auth passes ────────────────────────

    [Fact]
    public void ReceiveApproval_WhenValidApprove_CallsSubmitApprovalWithTrue()
    {
        var orchestrator = new RecordingOrchestrator();
        var controller   = BuildController(orchestrator, configuredSecret: ValidSecret);
        controller.ControllerContext = MakeContext(secretHeader: ValidSecret);

        var result = controller.ReceiveApproval(MakeBody("run-approve", "approve"));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, orchestrator.SubmitApprovalCallCount);
        Assert.Equal(("run-approve", true), orchestrator.LastSubmit);
    }

    [Fact]
    public void ReceiveApproval_WhenValidReject_CallsSubmitApprovalWithFalse()
    {
        var orchestrator = new RecordingOrchestrator();
        var controller   = BuildController(orchestrator, configuredSecret: ValidSecret);
        controller.ControllerContext = MakeContext(secretHeader: ValidSecret);

        var result = controller.ReceiveApproval(MakeBody("run-reject", "reject"));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(("run-reject", false), orchestrator.LastSubmit);
    }

    [Fact]
    public void ReceiveApproval_WhenBodyMissingRunId_ReturnsBadRequest()
    {
        var orchestrator = new RecordingOrchestrator();
        var controller   = BuildController(orchestrator, configuredSecret: ValidSecret);
        controller.ControllerContext = MakeContext(secretHeader: ValidSecret);

        // Body lacks "runId".
        var body = JsonDocument.Parse("""{"decision":"approve"}""").RootElement;

        var result = controller.ReceiveApproval(body);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, orchestrator.SubmitApprovalCallCount);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static TeamsWebhookController BuildController(
        IWorkflowExecutionOrchestrator orchestrator,
        string configuredSecret)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WebhookSecrets:Teams"] = configuredSecret,
            })
            .Build();

        return new TeamsWebhookController(orchestrator, config, NullLogger<TeamsWebhookController>.Instance);
    }

    private static ControllerContext MakeContext(string? secretHeader)
    {
        var httpContext = new DefaultHttpContext();
        if (secretHeader is not null)
            httpContext.Request.Headers["X-Teams-Secret"] = secretHeader;

        return new ControllerContext { HttpContext = httpContext };
    }

    private static JsonElement MakeBody(string runId, string decision)
    {
        var json = JsonSerializer.Serialize(new { runId, decision });
        return JsonDocument.Parse(json).RootElement;
    }

    // ── Fake orchestrator that records SubmitApproval calls ──────────────────

    private sealed class RecordingOrchestrator : IWorkflowExecutionOrchestrator
    {
        public int SubmitApprovalCallCount { get; private set; }
        public (string RunId, bool Approved)? LastSubmit { get; private set; }

        public void SubmitApproval(string runId, bool approved)
        {
            SubmitApprovalCallCount++;
            LastSubmit = (runId, approved);
        }

        // ── Minimal interface stubs ────────────────────────────────────────
        public event Action<string>? RunUpdated { add { } remove { } }
        public WorkflowExecutionRun? GetRun(string runId) => null;
        public Task<string> StartRunAsync(
            DBAIAzure.Core.Models.WorkflowDefinition workflow,
            string inputDescription,
            CancellationToken ct = default) => Task.FromResult(string.Empty);
        public void RequestStop(string runId) { }
        public void RehydratePausedRun(DBAIAzure.Core.Models.WorkflowRunRecord record) { }
    }
}
