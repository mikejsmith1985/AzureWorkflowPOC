// Live integration test for the Azure DevOps Boards round-trip the phase handler depends on.
// Skipped by default — it needs a real organization + PAT — so it never runs in normal CI.
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Proves the real Azure DevOps Work Item Tracking APIs the phase handler relies on actually work:
/// create an Epic, append a Discussion comment via System.History, and create a child Task linked to
/// the Epic. This exercises the same SDK surface as <c>AzureDevOpsBoardsClient</c>. It is intentionally
/// skipped — remove the Skip and set the env vars to run it against a throwaway Agile-process project.
/// </summary>
[Trait("Category", "Integration")]
public class AzureDevOpsBoardsClientTests
{
    // Field reference names are Azure DevOps system constants, not magic strings.
    private const string TitleField = "/fields/System.Title";
    private const string HistoryField = "/fields/System.History";
    private const string ParentLinkType = "System.LinkTypes.Hierarchy-Reverse";

    [Fact(Skip = "Integration: set AZDO_ORG_URL, AZDO_PROJECT, AZDO_PAT and remove this Skip to run a live Boards round-trip")]
    public async Task CreatesEpic_AppendsComment_AndLinksChildTask()
    {
        var organizationUrl = Environment.GetEnvironmentVariable("AZDO_ORG_URL")!;
        var projectName = Environment.GetEnvironmentVariable("AZDO_PROJECT")!;
        var personalAccessToken = Environment.GetEnvironmentVariable("AZDO_PAT")!;

        var connection = new VssConnection(
            new Uri(organizationUrl), new VssBasicCredential(string.Empty, personalAccessToken));
        var workItemClient = connection.GetClient<WorkItemTrackingHttpClient>();

        // Create the Epic.
        var epicPatch = new JsonPatchDocument
        {
            new JsonPatchOperation { Operation = Operation.Add, Path = TitleField, Value = "[Integration] phase-handler epic" },
        };
        var epic = await workItemClient.CreateWorkItemAsync(epicPatch, projectName, "Epic");
        Assert.NotNull(epic.Id);

        // Append a Discussion comment (append-only, creates a new revision).
        var commentPatch = new JsonPatchDocument
        {
            new JsonPatchOperation { Operation = Operation.Add, Path = HistoryField, Value = "Integration test comment" },
        };
        await workItemClient.UpdateWorkItemAsync(commentPatch, epic.Id!.Value);

        // Create a child Task linked under the Epic.
        var taskPatch = new JsonPatchDocument
        {
            new JsonPatchOperation { Operation = Operation.Add, Path = TitleField, Value = "[Integration] child task" },
            new JsonPatchOperation
            {
                Operation = Operation.Add,
                Path = "/relations/-",
                Value = new { rel = ParentLinkType, url = epic.Url, attributes = new { comment = "integration link" } },
            },
        };
        var childTask = await workItemClient.CreateWorkItemAsync(taskPatch, projectName, "Task");
        Assert.NotNull(childTask.Id);

        // Read the child back with its relations and assert the parent link is present.
        var reloaded = await workItemClient.GetWorkItemAsync(
            childTask.Id!.Value, expand: WorkItemExpand.Relations);
        Assert.Contains(reloaded.Relations, relation => relation.Rel == ParentLinkType);
    }
}
