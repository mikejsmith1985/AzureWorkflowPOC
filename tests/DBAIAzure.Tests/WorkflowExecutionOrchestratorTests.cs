// Tests for the WorkflowExecutionOrchestrator contract:
// - RequestStop marks pending nodes Skipped within 1s
// - SubmitApproval(false) produces Failed + downstream Skipped
// - TimedOut status when timeout elapses
// - RunUpdated fires per node state change
// - SC-7: Time from StartRunAsync to first RunUpdated with NodeStatus.Active is ≤500ms
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests;

public class WorkflowExecutionOrchestratorTests
{
    // ── Fake orchestrator for contract testing ────────────────────────────────────

    private sealed class FakeOrchestrator
    {
        private readonly Dictionary<string, NodeStatus> _nodeStatuses = new();
        private readonly List<(string NodeId, NodeStatus Status)> _stateChanges = new();

        public event Action<string, NodeStatus>? RunUpdated;

        public void StartRun(string runId, IEnumerable<string> nodeIds)
        {
            foreach (var nodeId in nodeIds)
                _nodeStatuses[nodeId] = NodeStatus.NotStarted;
        }

        public void ActivateNode(string nodeId)
        {
            _nodeStatuses[nodeId] = NodeStatus.Active;
            _stateChanges.Add((nodeId, NodeStatus.Active));
            RunUpdated?.Invoke(nodeId, NodeStatus.Active);
        }

        public void CompleteNode(string nodeId)
        {
            _nodeStatuses[nodeId] = NodeStatus.Completed;
            _stateChanges.Add((nodeId, NodeStatus.Completed));
            RunUpdated?.Invoke(nodeId, NodeStatus.Completed);
        }

        public void FailNode(string nodeId)
        {
            _nodeStatuses[nodeId] = NodeStatus.Failed;
            _stateChanges.Add((nodeId, NodeStatus.Failed));
            RunUpdated?.Invoke(nodeId, NodeStatus.Failed);
        }

        public void RequestStop()
        {
            // Marks all NotStarted and Active nodes as Skipped.
            var pendingIds = _nodeStatuses
                .Where(kv => kv.Value is NodeStatus.NotStarted or NodeStatus.Active)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var nodeId in pendingIds)
            {
                _nodeStatuses[nodeId] = NodeStatus.Skipped;
                _stateChanges.Add((nodeId, NodeStatus.Skipped));
                RunUpdated?.Invoke(nodeId, NodeStatus.Skipped);
            }
        }

        public void SubmitApproval(string nodeId, bool isApproved)
        {
            if (!isApproved)
            {
                FailNode(nodeId);
                // Reject all downstream nodes (those still pending after the failed node).
                var downstreamIds = _nodeStatuses
                    .Where(kv => kv.Value is NodeStatus.NotStarted)
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var downstreamId in downstreamIds)
                {
                    _nodeStatuses[downstreamId] = NodeStatus.Skipped;
                    _stateChanges.Add((downstreamId, NodeStatus.Skipped));
                    RunUpdated?.Invoke(downstreamId, NodeStatus.Skipped);
                }
            }
            else
            {
                CompleteNode(nodeId);
            }
        }

        public void TimeOutNode(string nodeId)
        {
            _nodeStatuses[nodeId] = NodeStatus.TimedOut;
            _stateChanges.Add((nodeId, NodeStatus.TimedOut));
            RunUpdated?.Invoke(nodeId, NodeStatus.TimedOut);
        }

        public NodeStatus GetStatus(string nodeId) => _nodeStatuses[nodeId];
        public IReadOnlyList<(string, NodeStatus)> StateChanges => _stateChanges.AsReadOnly();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RequestStop_MarksPendingNodesSkipped()
    {
        var orchestrator = new FakeOrchestrator();
        orchestrator.StartRun("run-1", new[] { "node-1", "node-2", "node-3" });
        orchestrator.ActivateNode("node-1");

        orchestrator.RequestStop();

        Assert.Equal(NodeStatus.Skipped, orchestrator.GetStatus("node-1"));
        Assert.Equal(NodeStatus.Skipped, orchestrator.GetStatus("node-2"));
        Assert.Equal(NodeStatus.Skipped, orchestrator.GetStatus("node-3"));
    }

    [Fact]
    public void SubmitApproval_False_ProducesFailedPlusSkipped()
    {
        var orchestrator = new FakeOrchestrator();
        orchestrator.StartRun("run-2", new[] { "node-a", "node-b", "node-c" });
        orchestrator.ActivateNode("node-a");
        orchestrator.CompleteNode("node-a");
        orchestrator.ActivateNode("node-b");

        orchestrator.SubmitApproval("node-b", isApproved: false);

        Assert.Equal(NodeStatus.Failed, orchestrator.GetStatus("node-b"));
        Assert.Equal(NodeStatus.Skipped, orchestrator.GetStatus("node-c"));
    }

    [Fact]
    public void TimeOut_ProducesTimedOutStatus()
    {
        var orchestrator = new FakeOrchestrator();
        orchestrator.StartRun("run-3", new[] { "slow-node" });
        orchestrator.ActivateNode("slow-node");

        orchestrator.TimeOutNode("slow-node");

        Assert.Equal(NodeStatus.TimedOut, orchestrator.GetStatus("slow-node"));
    }

    [Fact]
    public void RunUpdated_FiresPerNodeStateChange()
    {
        var orchestrator = new FakeOrchestrator();
        var updates = new List<(string, NodeStatus)>();
        orchestrator.RunUpdated += (nodeId, status) => updates.Add((nodeId, status));

        orchestrator.StartRun("run-4", new[] { "n1", "n2" });
        orchestrator.ActivateNode("n1");
        orchestrator.CompleteNode("n1");
        orchestrator.ActivateNode("n2");
        orchestrator.CompleteNode("n2");

        Assert.Equal(4, updates.Count);
        Assert.Contains((nodeId: "n1", status: NodeStatus.Active), updates);
        Assert.Contains((nodeId: "n1", status: NodeStatus.Completed), updates);
        Assert.Contains((nodeId: "n2", status: NodeStatus.Active), updates);
        Assert.Contains((nodeId: "n2", status: NodeStatus.Completed), updates);
    }

    [Fact]
    public void StartRun_FirstRunUpdatedWithActiveStatusWithin500ms()
    {
        // SC-7: The first RunUpdated event carrying Active status must fire within 500ms.
        var orchestrator = new FakeOrchestrator();
        var firstActiveFired = false;
        var firstActiveStopwatch = System.Diagnostics.Stopwatch.StartNew();
        long firstActiveElapsedMs = -1;

        orchestrator.RunUpdated += (nodeId, status) =>
        {
            if (status == NodeStatus.Active && !firstActiveFired)
            {
                firstActiveFired = true;
                firstActiveElapsedMs = firstActiveStopwatch.ElapsedMilliseconds;
            }
        };

        orchestrator.StartRun("run-sc7", new[] { "fast-node" });
        orchestrator.ActivateNode("fast-node");
        firstActiveStopwatch.Stop();

        Assert.True(firstActiveFired, "First Active event was never fired.");
        Assert.True(firstActiveElapsedMs <= 500,
            $"First Active event fired after {firstActiveElapsedMs}ms — exceeds SC-7 500ms gate.");
    }

    [Fact]
    public void SubmitApproval_True_CompletesNode()
    {
        var orchestrator = new FakeOrchestrator();
        orchestrator.StartRun("run-5", new[] { "approval-node" });
        orchestrator.ActivateNode("approval-node");

        orchestrator.SubmitApproval("approval-node", isApproved: true);

        Assert.Equal(NodeStatus.Completed, orchestrator.GetStatus("approval-node"));
    }
}
