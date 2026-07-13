// spec-019 T037: the Run Detail Stream tab renders the live token stream a MAF run produces. Drives a
// real intake run on the MAF runtime (pinned RecordedChatClient), then renders RunDetail and switches to
// the Stream tab, asserting the streamed tokens appear — the UI half of the T038 streaming parity.
using Bunit;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Tests.Parity;
using DBAIAzure.Web.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// bUnit test for <c>RunDetail.razor</c>'s Stream tab. A ready ticket runs the intake pipeline on the MAF
/// runtime, whose LLM executors stream tokens through the run-bound reporter into
/// <see cref="PipelineRun.TokenStream"/>. Rendering the page and selecting the Stream tab must show those
/// tokens grouped by step — proving the live-streaming UI is driven by the migrated MAF path.
/// </summary>
public sealed class RunDetailStreamTabTests : TestContext
{
    [Fact]
    public async Task StreamTab_ShowsStreamedTokens_ForMafRun()
    {
        // The Graph tab renders a Mermaid diagram via JS; the Stream tab does not, but keep JS loose so an
        // incidental interop call from a re-render never fails the test.
        JSInterop.Mode = JSRuntimeMode.Loose;

        var chatClient = new RecordedChatClient(new[]
        {
            RecordedTurn.With("{\"title\":\"Sample\",\"description\":\"Sample description.\"}", 40, 12),
            RecordedTurn.With("{\"is_ready\":true,\"missing_fields\":[],\"reasoning\":\"clear\"}", 30, 8),
            RecordedTurn.With("{\"points\":5,\"reasoning\":\"comparable to the CRUD anchor\"}", 25, 8),
        }, repeatLast: true);

        var orchestrator = new PipelineOrchestrator(chatClient);
        var ticket = new TicketState { TicketId = "INC0001", Title = "Sample", Description = "Sample description." };
        var runId = orchestrator.StartRun(ticket);

        // The run is fire-and-forget; wait until the streaming executors have enqueued tokens.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && (orchestrator.GetRun(runId)?.TokenStream.IsEmpty ?? true))
        {
            await Task.Delay(25);
        }

        Assert.False(orchestrator.GetRun(runId)?.TokenStream.IsEmpty ?? true, "no tokens were streamed");

        Services.AddSingleton(orchestrator);
        var cut = RenderComponent<RunDetail>(parameters => parameters.Add(component => component.RunId, runId));

        // Switch to the Stream tab.
        var streamTab = cut.FindAll("button").First(button => button.TextContent.Contains("Stream"));
        streamTab.Click();

        Assert.Contains("[Intake]", cut.Markup);   // tokens are grouped under the streaming step
        Assert.Contains("Sample", cut.Markup);     // the streamed model text is rendered
    }
}
