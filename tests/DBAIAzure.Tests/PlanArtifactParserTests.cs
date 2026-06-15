// Tests for PlanArtifactParser: tasks.md task lines → PlannedWorkItem[]; fallback to plan.md sections.
using DBAIAzure.Core.Models;
using DBAIAzure.Processes;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Verifies the pure markdown parsing that turns Plan-phase artifacts into planned units of work
/// (FR-008): one <see cref="PlannedWorkItem"/> per <c>tasks.md</c> task line when present, otherwise
/// one per structural <c>##</c>/<c>###</c> section of <c>plan.md</c>. No I/O — text in, items out.
/// </summary>
public class PlanArtifactParserTests
{
    [Fact]
    public void Parse_TasksMd_OneItemPerTaskLine()
    {
        var tasks = """
            # Tasks

            - [ ] T001 Set up the project skeleton
            - [ ] T002 [P] Create the domain models
            - [X] T003 Wire dependency injection
            """;
        var artifacts = new List<PhaseArtifact>
        {
            new() { FileName = "tasks.md", Content = tasks },
            new() { FileName = "plan.md", Content = "## Ignored when tasks.md exists" },
        };

        var items = PlanArtifactParser.Parse(artifacts);

        Assert.Equal(3, items.Count);
        Assert.Equal("T001 Set up the project skeleton", items[0].Title);
        // The [P] parallel marker is stripped from the title.
        Assert.Equal("T002 Create the domain models", items[1].Title);
        Assert.Equal("T003 Wire dependency injection", items[2].Title);
    }

    [Fact]
    public void Parse_TasksMd_CapturesTaskBodyInDescription()
    {
        var tasks = "- [ ] T001 Set up the project skeleton in src/App";
        var artifacts = new List<PhaseArtifact> { new() { FileName = "tasks.md", Content = tasks } };

        var items = PlanArtifactParser.Parse(artifacts);

        Assert.Single(items);
        Assert.Contains("Set up the project skeleton in src/App", items[0].Description);
    }

    [Fact]
    public void Parse_NoTasksMd_DerivesFromPlanSections()
    {
        var plan = """
            # Implementation Plan

            ## Architecture
            The system uses a layered design.

            ## Data Model
            Entities are immutable records.

            ### Sub-detail
            A nested heading also counts as a unit.
            """;
        var artifacts = new List<PhaseArtifact> { new() { FileName = "plan.md", Content = plan } };

        var items = PlanArtifactParser.Parse(artifacts);

        Assert.Equal(3, items.Count);
        Assert.Equal("Architecture", items[0].Title);
        Assert.Equal("Data Model", items[1].Title);
        Assert.Equal("Sub-detail", items[2].Title);
        Assert.Contains("layered design", items[0].Description);
    }

    [Fact]
    public void Parse_NoArtifacts_ReturnsEmpty()
    {
        var items = PlanArtifactParser.Parse([]);
        Assert.Empty(items);
    }

    [Fact]
    public void Parse_PlanWithNoHeadings_ReturnsEmpty()
    {
        var artifacts = new List<PhaseArtifact>
        {
            new() { FileName = "plan.md", Content = "Just prose, no headings at all." },
        };
        var items = PlanArtifactParser.Parse(artifacts);
        Assert.Empty(items);
    }

    [Fact]
    public void Parse_TasksMd_IgnoresNonTaskLines()
    {
        var tasks = """
            # Tasks

            Some preamble prose that is not a task.

            - [ ] T001 The only real task
            - not a checkbox bullet
            """;
        var artifacts = new List<PhaseArtifact> { new() { FileName = "tasks.md", Content = tasks } };

        var items = PlanArtifactParser.Parse(artifacts);

        Assert.Single(items);
        Assert.Equal("T001 The only real task", items[0].Title);
    }

    [Fact]
    public void Parse_TasksMdWithMoreThanCapItems_FallsBackToPlanMdSections()
    {
        // tasks.md produced by /speckit.tasks can have 50+ implementation-level tasks; using all of
        // them as Plan-phase ADO board items floods the board. When the count exceeds the cap the
        // parser must fall through to plan.md section headings (plan-level granularity).
        var manyTasks = string.Join("\n", Enumerable.Range(1, 25)
            .Select(i => $"- [ ] T{i:D3} Implementation task number {i}"));
        var artifacts = new List<PhaseArtifact>
        {
            new() { FileName = "tasks.md", Content = manyTasks },
            new() { FileName = "plan.md", Content = "## Architecture\n\n## Data Layer" },
        };

        var items = PlanArtifactParser.Parse(artifacts);

        Assert.Equal(2, items.Count);
        Assert.Equal("Architecture", items[0].Title);
        Assert.Equal("Data Layer", items[1].Title);
    }
}
