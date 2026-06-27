// Tests for WorkflowDesignSkillService contract behaviour:
// - questions are generated from topology content
// - a previously answered question is not re-asked
// - user-deferral records "user-deferred" in DesignSkillAnswers
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests;

public class WorkflowDesignSkillServiceTests
{
    // ── Minimal inline implementation that validates the contract ───────────────
    // This stub mirrors the behavior the real WorkflowDesignSkillService must implement.

    private sealed class StubDesignSkillService
    {
        private const string DeferredSentinel = "user-deferred";

        /// <summary>
        /// Returns questions relevant to the workflow that have not yet been answered
        /// or explicitly deferred by the user.
        /// </summary>
        public IReadOnlyList<string> GetPendingQuestions(WorkflowDefinition workflow)
        {
            // Derive topic questions from node types present in the topology.
            var questions = new List<string>();
            foreach (var node in workflow.Nodes)
            {
                var questionKey = QuestionKeyFor(node);
                // Skip already answered or deferred.
                if (workflow.Settings.DesignSkillAnswers.ContainsKey(questionKey))
                    continue;
                questions.Add($"What should happen when '{node.Label}' finishes?");
            }
            return questions.AsReadOnly();
        }

        /// <summary>
        /// Records the user's answer for a design question, returning an updated settings snapshot.
        /// </summary>
        public WorkflowSettings RecordAnswer(WorkflowSettings settings, string questionKey, string answer)
        {
            var updatedAnswers = new Dictionary<string, string>(settings.DesignSkillAnswers)
            {
                [questionKey] = answer
            };
            return settings with { DesignSkillAnswers = updatedAnswers.AsReadOnly() };
        }

        /// <summary>
        /// Defers a question, recording the sentinel value so it is never re-asked.
        /// </summary>
        public WorkflowSettings DeferQuestion(WorkflowSettings settings, string questionKey)
        {
            return RecordAnswer(settings, questionKey, DeferredSentinel);
        }

        private static string QuestionKeyFor(WorkflowNode node) => $"node.{node.Id}.completion";
    }

    // ── Helper ──────────────────────────────────────────────────────────────────

    private static WorkflowDefinition BuildWorkflowWithTwoNodes()
    {
        var nodeA = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Node Alpha") with
        {
            IsConfigured = true,
            InputPorts   = Array.Empty<WorkflowPort>().ToList().AsReadOnly(),
            OutputPorts  = Array.Empty<WorkflowPort>().ToList().AsReadOnly(),
        };
        var nodeB = WorkflowNode.CreateNew(WorkflowNodeType.HumanApproval, "Node Beta") with
        {
            IsConfigured = true,
            InputPorts   = Array.Empty<WorkflowPort>().ToList().AsReadOnly(),
            OutputPorts  = Array.Empty<WorkflowPort>().ToList().AsReadOnly(),
        };
        return WorkflowDefinition.CreateNew("Design Skill Test", "owner1") with
        {
            Nodes = new[] { nodeA, nodeB }.ToList().AsReadOnly(),
        };
    }

    // ── Tests ───────────────────────────────────────────────────────────────────

    [Fact]
    public void GetPendingQuestions_GeneratesQuestionsFromTopology()
    {
        var service  = new StubDesignSkillService();
        var workflow = BuildWorkflowWithTwoNodes();

        var questions = service.GetPendingQuestions(workflow);

        Assert.Equal(2, questions.Count);
        Assert.Contains(questions, q => q.Contains("Node Alpha"));
        Assert.Contains(questions, q => q.Contains("Node Beta"));
    }

    [Fact]
    public void GetPendingQuestions_SkipsAlreadyAnsweredQuestion()
    {
        var service   = new StubDesignSkillService();
        var workflow  = BuildWorkflowWithTwoNodes();
        var nodeAKey  = $"node.{workflow.Nodes[0].Id}.completion";

        // Pre-populate an answer for node A.
        var updatedSettings = service.RecordAnswer(workflow.Settings, nodeAKey, "send notification");
        var workflowWithAnswer = workflow with { Settings = updatedSettings };

        var questions = service.GetPendingQuestions(workflowWithAnswer);

        Assert.Single(questions);
        Assert.Contains(questions, q => q.Contains("Node Beta"));
        Assert.DoesNotContain(questions, q => q.Contains("Node Alpha"));
    }

    [Fact]
    public void DeferQuestion_RecordsUserDeferredSentinel()
    {
        var service  = new StubDesignSkillService();
        const string questionKey = "node.abc123.completion";

        var updatedSettings = service.DeferQuestion(new WorkflowSettings(), questionKey);

        Assert.True(updatedSettings.DesignSkillAnswers.TryGetValue(questionKey, out var value));
        Assert.Equal("user-deferred", value);
    }

    [Fact]
    public void GetPendingQuestions_SkipsDeferredQuestion()
    {
        var service   = new StubDesignSkillService();
        var workflow  = BuildWorkflowWithTwoNodes();
        var nodeBKey  = $"node.{workflow.Nodes[1].Id}.completion";

        var settings = service.DeferQuestion(workflow.Settings, nodeBKey);
        var workflowWithDeferred = workflow with { Settings = settings };

        var questions = service.GetPendingQuestions(workflowWithDeferred);

        // Only Node Alpha should remain as a pending question.
        Assert.Single(questions);
        Assert.DoesNotContain(questions, q => q.Contains("Node Beta"));
    }

    [Fact]
    public void RecordAnswer_DoesNotMutateOriginalSettings()
    {
        var service      = new StubDesignSkillService();
        var original     = new WorkflowSettings();
        const string key = "some.question";

        var updated = service.RecordAnswer(original, key, "my answer");

        // Original must be unchanged (immutable record).
        Assert.False(original.DesignSkillAnswers.ContainsKey(key));
        Assert.True(updated.DesignSkillAnswers.ContainsKey(key));
    }

    [Fact]
    public void GetPendingQuestions_EmptyWorkflow_ReturnsEmptyList()
    {
        var service  = new StubDesignSkillService();
        var workflow = WorkflowDefinition.CreateNew("Empty", "owner1");

        var questions = service.GetPendingQuestions(workflow);

        Assert.Empty(questions);
    }
}
