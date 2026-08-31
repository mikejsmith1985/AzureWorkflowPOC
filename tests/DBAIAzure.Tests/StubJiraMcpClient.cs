// Test double for the Jira MCP transport. Defaults to "MCP not configured" so existing adapter tests keep
// exercising the REST fallback path, and can be told to answer instead to prove MCP is preferred.
using DBAIAzure.Core.Models.WorkTracker;
using DBAIAzure.Web.Integrations.Jira;

namespace DBAIAzure.Tests;

/// <summary>
/// Hand-rolled <see cref="IJiraMcpClient"/> stub. With no answers supplied every method reports "unavailable",
/// which is exactly what the adapter sees when an operator has not configured an MCP server.
/// </summary>
public sealed class StubJiraMcpClient : IJiraMcpClient
{
    /// <summary>Issue returned by <see cref="TryReadIssueAsync"/>; null means MCP cannot serve the read.</summary>
    public WorkItemFields? ReadResult { get; set; }

    /// <summary>Whether <see cref="TryTransitionAsync"/> reports the transition as done over MCP.</summary>
    public bool CanTransition { get; set; }

    /// <summary>Issues returned by <see cref="SearchAsync"/>.</summary>
    public IReadOnlyList<JiraIssueSummary> SearchResults { get; set; } = Array.Empty<JiraIssueSummary>();

    /// <summary>Transition ids this stub was asked for, so a test can assert MCP was tried first.</summary>
    public List<string> RequestedTransitions { get; } = [];

    /// <summary>Issue keys this stub was asked to read.</summary>
    public List<string> RequestedReads { get; } = [];

    public Task<bool> IsEnabledAsync(CancellationToken ct = default) =>
        Task.FromResult(ReadResult is not null || CanTransition || SearchResults.Count > 0);

    public Task<WorkItemFields?> TryReadIssueAsync(
        string issueKey, IReadOnlyCollection<string> watchFields, CancellationToken ct = default)
    {
        RequestedReads.Add(issueKey);
        return Task.FromResult(ReadResult);
    }

    public Task<bool> TryTransitionAsync(string issueKey, string transitionId, CancellationToken ct = default)
    {
        RequestedTransitions.Add(transitionId);
        return Task.FromResult(CanTransition);
    }

    public Task<IReadOnlyList<JiraIssueSummary>> SearchAsync(
        string jql, int maxResults, CancellationToken ct = default) =>
        Task.FromResult(SearchResults);
}
