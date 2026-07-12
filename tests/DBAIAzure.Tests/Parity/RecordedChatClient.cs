// A deterministic record/replay IChatClient so parity tests assert framework equivalence against a
// PINNED model output rather than a live, non-deterministic model (spec-019 T002a; resolves finding U1).
using Microsoft.Extensions.AI;

namespace DBAIAzure.Tests.Parity;

/// <summary>
/// One pinned model turn: the assistant text plus the exact token <see cref="UsageDetails"/> that a real
/// provider would have reported. Parity and cost tests assert against these fixed values so equivalence
/// between the pre-migration (SK) and post-migration (MAF/M.E.AI) builds is falsifiable — no live LLM.
/// </summary>
/// <param name="Text">The assistant response text to replay (also the concatenation of streamed chunks).</param>
/// <param name="Usage">The token usage to attach to the response (and the final streaming update).</param>
/// <param name="ModelId">The model id the response advertises, matching what a real call would return.</param>
public sealed record RecordedTurn(string Text, UsageDetails Usage, string ModelId = "claude-opus-4-8")
{
    /// <summary>Convenience factory building a turn's <see cref="UsageDetails"/> from raw token counts.</summary>
    public static RecordedTurn With(
        string text,
        int inputTokens,
        int outputTokens,
        int cacheReadTokens = 0,
        int cacheCreationTokens = 0,
        string modelId = "claude-opus-4-8")
    {
        var usage = new UsageDetails
        {
            InputTokenCount = inputTokens,
            OutputTokenCount = outputTokens,
            TotalTokenCount = inputTokens + outputTokens,
            CachedInputTokenCount = cacheReadTokens,
        };

        // Anthropic surfaces cache-creation tokens as an extra count; mirror that key so a consumer that
        // reads AdditionalCounts (the cost client) sees the same shape a real Anthropic call produces.
        if (cacheCreationTokens > 0)
        {
            usage.AdditionalCounts ??= new AdditionalPropertiesDictionary<long>();
            usage.AdditionalCounts["cache_creation_input_tokens"] = cacheCreationTokens;
        }

        return new RecordedTurn(text, usage, modelId);
    }
}

/// <summary>
/// A deterministic <see cref="IChatClient"/> that replays a fixed script of <see cref="RecordedTurn"/>s —
/// once per call, in order — for both the non-streaming and streaming paths. It never touches the
/// network, so a parity test pins the model's output and usage and asserts that the migrated framework
/// produces the same observable result. The client also records every request it received so tests can
/// assert on the prompt/options the pipeline sent.
/// </summary>
public sealed class RecordedChatClient : IChatClient
{
    private readonly IReadOnlyList<RecordedTurn> _script;
    private readonly bool _repeatLast;
    private int _callIndex;

    private readonly List<RecordedRequest> _requests = new();

    /// <summary>Every request the client received, in call order — for prompt/options assertions.</summary>
    public IReadOnlyList<RecordedRequest> Requests => _requests;

    /// <summary>Number of times the client has been invoked (streaming or not).</summary>
    public int CallCount => _callIndex;

    /// <summary>
    /// Creates the client over a fixed script. When <paramref name="repeatLast"/> is true, calls beyond
    /// the script length replay the final turn (useful when a pipeline makes an unknown number of calls);
    /// otherwise an extra call throws so an unexpected call is caught by the test rather than masked.
    /// </summary>
    public RecordedChatClient(IEnumerable<RecordedTurn> script, bool repeatLast = false)
    {
        _script = script.ToList();
        _repeatLast = repeatLast;
        if (_script.Count == 0)
        {
            throw new ArgumentException("A recorded script must contain at least one turn.", nameof(script));
        }
    }

    /// <summary>Creates a client that replays a single turn for every call.</summary>
    public RecordedChatClient(RecordedTurn singleTurn) : this(new[] { singleTurn }, repeatLast: true) { }

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var turn = NextTurn(messages, options, isStreaming: false);
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, turn.Text))
        {
            ModelId = turn.ModelId,
            Usage = turn.Usage,
        };
        return Task.FromResult(response);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var turn = NextTurn(messages, options, isStreaming: true);

        // Stream the text as a single content update, then a terminal usage-only update — the same shape
        // M.E.AI produces, where usage rides the final ChatResponseUpdate as a UsageContent.
        yield return new ChatResponseUpdate(ChatRole.Assistant, turn.Text) { ModelId = turn.ModelId };
        await Task.Yield();
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            ModelId = turn.ModelId,
            Contents = new List<AIContent> { new UsageContent(turn.Usage) },
        };
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    /// <inheritdoc />
    public void Dispose() { }

    /// <summary>Records the request and returns the scripted turn for the current call index.</summary>
    private RecordedTurn NextTurn(IEnumerable<ChatMessage> messages, ChatOptions? options, bool isStreaming)
    {
        var snapshot = messages.ToList();
        _requests.Add(new RecordedRequest(snapshot, options, isStreaming));

        var index = _callIndex++;
        if (index < _script.Count)
        {
            return _script[index];
        }

        if (_repeatLast)
        {
            return _script[^1];
        }

        throw new InvalidOperationException(
            $"RecordedChatClient received call #{index + 1} but only {_script.Count} turn(s) were scripted.");
    }
}

/// <summary>One request captured by <see cref="RecordedChatClient"/> for later assertion.</summary>
/// <param name="Messages">The rendered prompt the pipeline sent (system + turns).</param>
/// <param name="Options">The chat options (model, response format, tools) the pipeline set, if any.</param>
/// <param name="IsStreaming">True when the request came through the streaming path.</param>
public sealed record RecordedRequest(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options, bool IsStreaming);
