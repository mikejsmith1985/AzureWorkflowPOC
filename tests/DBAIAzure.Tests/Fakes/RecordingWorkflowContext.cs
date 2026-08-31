// A recording IWorkflowContext so MAF executors can be unit-tested without standing up a workflow run.

using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Tests.Fakes;

/// <summary>
/// An in-memory <see cref="IWorkflowContext"/> that records what an executor sent, yielded, and halted on,
/// so a test can assert an executor's routing decision directly. MAF's real context needs a running workflow;
/// an executor's branch logic does not, and this keeps those tests in the 100%-mocked unit layer (Article V).
/// <para>State operations are backed by a plain dictionary — enough for executors that checkpoint, and
/// inert for those that do not.</para>
/// </summary>
public sealed class RecordingWorkflowContext : IWorkflowContext
{
    private readonly Dictionary<string, object?> _state = [];

    /// <summary>No distributed trace is in flight for a unit test, so no context is offered.</summary>
    public IReadOnlyDictionary<string, string>? TraceContext => null;

    /// <summary>A single-threaded fake never runs concurrently against the same instance.</summary>
    public bool ConcurrentRunsEnabled => false;

    /// <summary>Messages forwarded to the next executor via <c>SendMessageAsync</c>, in order.</summary>
    public List<object> SentMessages { get; } = [];

    /// <summary>Terminal outputs yielded via <c>YieldOutputAsync</c>, in order.</summary>
    public List<object> YieldedOutputs { get; } = [];

    /// <summary>Events raised via <c>AddEventAsync</c>, in order.</summary>
    public List<WorkflowEvent> Events { get; } = [];

    /// <summary>True once the executor asked the workflow to stop.</summary>
    public bool WasHaltRequested { get; private set; }

    public ValueTask SendMessageAsync(object message, string? targetId = null, CancellationToken cancellationToken = default)
    {
        SentMessages.Add(message);
        return default;
    }

    public ValueTask YieldOutputAsync(object output, CancellationToken cancellationToken = default)
    {
        YieldedOutputs.Add(output);
        return default;
    }

    public ValueTask AddEventAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(workflowEvent);
        return default;
    }

    public ValueTask RequestHaltAsync()
    {
        WasHaltRequested = true;
        return default;
    }

    public ValueTask QueueStateUpdateAsync<TValue>(string key, TValue? value, string? scopeName = null, CancellationToken cancellationToken = default)
    {
        _state[Scoped(key, scopeName)] = value;
        return default;
    }

    public ValueTask<TValue?> ReadStateAsync<TValue>(string key, string? scopeName = null, CancellationToken cancellationToken = default)
        => new(_state.TryGetValue(Scoped(key, scopeName), out var value) ? (TValue?)value : default);

    public ValueTask<TValue> ReadOrInitStateAsync<TValue>(string key, Func<TValue>? defaultValueFactory = null, string? scopeName = null, CancellationToken cancellationToken = default)
    {
        var scoped = Scoped(key, scopeName);
        if (!_state.TryGetValue(scoped, out var value))
        {
            value = defaultValueFactory is not null ? defaultValueFactory() : default(TValue);
            _state[scoped] = value;
        }
        return new((TValue)value!);
    }

    public ValueTask<HashSet<string>> ReadStateKeysAsync(string? scopeName = null, CancellationToken cancellationToken = default)
    {
        var prefix = scopeName is null ? string.Empty : scopeName + "::";
        return new(_state.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                              .Select(k => k[prefix.Length..]).ToHashSet());
    }

    public ValueTask QueueClearScopeAsync(string? scopeName = null, CancellationToken cancellationToken = default)
    {
        var prefix = scopeName is null ? string.Empty : scopeName + "::";
        foreach (var key in _state.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            _state.Remove(key);
        return default;
    }

    /// <summary>Namespaces a key by its scope so two scopes cannot collide in the flat backing dictionary.</summary>
    private static string Scoped(string key, string? scopeName) => scopeName is null ? key : scopeName + "::" + key;
}
