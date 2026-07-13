// Performance-budget check (spec-019 T055 / SC-010): the migrated MAF intake pipeline's end-to-end run
// latency must be within 10% of the retired SK Process Framework baseline on the same host, with the model
// held constant. The model is a deterministic, instant scripted stub on BOTH paths, so the measured delta is
// pure framework/orchestration overhead (SK Process Framework vs MAF Workflows) — the model's own latency,
// identical on both, is factored out. Each framework is driven DIRECTLY to completion (awaited, no
// fire-and-forget polling) so the measurement is the real per-run execution time, not a poll-granularity
// artifact. Tagged [Perf] so it is excluded from the normal suite (it is a manual gate, not a unit test).
#pragma warning disable SKEXP0080

using System.Diagnostics;
using System.Runtime.CompilerServices;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes;
using DBAIAzure.Processes.Executors;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Processes.Pipeline.Maf;
using DBAIAzure.Processes.Steps;
using DBAIAzure.Tests.Parity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Xunit;
using Xunit.Abstractions;

namespace DBAIAzure.Tests.Perf;

/// <summary>
/// Measures and compares the per-run latency of the SK and MAF intake pipelines with an identical instant
/// model, over many iterations, and asserts MAF is within the 10% budget of the SK baseline (SC-010).
/// </summary>
[Trait("Category", "Perf")]
public sealed class FrameworkPerfBaselineTests
{
    private readonly ITestOutputHelper _output;

    public FrameworkPerfBaselineTests(ITestOutputHelper output) => _output = output;

    // The three pinned model turns a ready-ticket intake run makes: normalise → decide READY → estimate.
    private static readonly string[] Script =
    {
        "{\"title\":\"Sample\",\"description\":\"Sample description.\"}",
        "{\"is_ready\":true,\"missing_fields\":[],\"reasoning\":\"clear\"}",
        "{\"points\":5,\"reasoning\":\"comparable to the CRUD anchor\"}",
    };

    private static TicketState SampleTicket => new()
    {
        TicketId = "INC0001",
        Title = "Sample",
        Description = "Sample description.",
    };

    private const int WarmupRuns = 5;
    private const int TimedRuns = 40;
    private const int ModelCallsPerRun = 3; // Intake, Validation (ready), Estimation

    [Fact]
    public async Task MafIntakeLatency_IsWithin10Percent_OfSkBaseline()
    {
        // Warm up both frameworks so JIT/assembly-load cost is not charged to the timed runs.
        for (var i = 0; i < WarmupRuns; i++)
        {
            await RunSkOnceAsync();
            await RunMafOnceAsync();
        }

        var skSamples = new List<double>(TimedRuns);
        var mafSamples = new List<double>(TimedRuns);

        // Interleave the two paths so transient host noise averages across both rather than biasing one.
        for (var i = 0; i < TimedRuns; i++)
        {
            skSamples.Add(await RunSkOnceAsync());
            mafSamples.Add(await RunMafOnceAsync());
        }

        var sk = Stats.From(skSamples);
        var maf = Stats.From(mafSamples);
        var deltaPercent = (maf.Median - sk.Median) / sk.Median * 100.0;

        var report =
            $"MAF modernization performance baseline (spec-019 T055 / SC-010)\n" +
            $"Model held constant (instant scripted stub on both paths); {TimedRuns} timed runs, {WarmupRuns} warmups.\n\n" +
            $"Per-run latency (ms):\n" +
            $"  SK  Process Framework : median={sk.Median:F3}  mean={sk.Mean:F3}  p90={sk.P90:F3}  min={sk.Min:F3}\n" +
            $"  MAF Workflows         : median={maf.Median:F3}  mean={maf.Mean:F3}  p90={maf.P90:F3}  min={maf.Min:F3}\n\n" +
            $"Per-model-call overhead (median/{ModelCallsPerRun} calls):\n" +
            $"  SK  : {sk.Median / ModelCallsPerRun:F3} ms/call\n" +
            $"  MAF : {maf.Median / ModelCallsPerRun:F3} ms/call\n\n" +
            $"Median delta: {deltaPercent:+0.0;-0.0}%   Budget: <= +10%   " +
            $"Verdict: {(deltaPercent <= 10.0 ? "WITHIN BUDGET" : "REGRESSION — BLOCKS CUTOVER")}\n";

        _output.WriteLine(report);
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "maf-perf-baseline.txt"), report);

        Assert.True(deltaPercent <= 10.0,
            $"MAF median run latency exceeded the 10% budget over the SK baseline.\n\n{report}");
    }

    // ── SK path: build the SK process + a scripted-model kernel and run it to completion. ──

    private static async Task<double> RunSkOnceAsync()
    {
        var run = new PipelineRun("perf-sk", SampleTicket);
        var reporter = new BoundProgressReporter(run, static () => { }, NullRunRepository.Instance);

        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton<IChatCompletionService>(new ScriptedChatCompletionService(Script));
        builder.Services.AddSingleton<IProgressReporter>(reporter);
        var kernel = builder.Build();

        var process = IntakePipelineBuilder.Build();
        var startEvent = new KernelProcessEvent { Id = Events.TicketReceived, Data = SampleTicket };

        var stopwatch = Stopwatch.StartNew();
        await LocalKernelProcessFactory.RunToEndAsync(
            process, kernel, startEvent, TimeSpan.FromSeconds(30), new HitlExternalChannel());
        stopwatch.Stop();

        return stopwatch.Elapsed.TotalMilliseconds;
    }

    // ── MAF path: build the MAF workflow with a scripted IChatClient and drive it to completion. ──

    private static async Task<double> RunMafOnceAsync()
    {
        var chatClient = new RecordedChatClient(
            Script.Select(text => RecordedTurn.With(text, inputTokens: 30, outputTokens: 8)).ToList(),
            repeatLast: true);
        var workflow = MafIntakeWorkflowFactory.Build(chatClient);

        var stopwatch = Stopwatch.StartNew();
        _ = await MafWorkflowRunner.RunAsync(workflow, SampleTicket);
        stopwatch.Stop();

        return stopwatch.Elapsed.TotalMilliseconds;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────

    private readonly record struct Stats(double Median, double Mean, double P90, double Min)
    {
        public static Stats From(IReadOnlyList<double> samples)
        {
            var sorted = samples.OrderBy(value => value).ToArray();
            double Percentile(double fraction) => sorted[Math.Clamp((int)(fraction * (sorted.Length - 1)), 0, sorted.Length - 1)];
            return new Stats(
                Median: Percentile(0.50),
                Mean: sorted.Average(),
                P90: Percentile(0.90),
                Min: sorted[0]);
        }
    }

    /// <summary>
    /// A deterministic, instant <see cref="IChatCompletionService"/> that replays a fixed script — the SK-side
    /// equivalent of the MAF <see cref="RecordedChatClient"/>, so both frameworks see the same zero-latency
    /// model and the measured difference is purely orchestration overhead.
    /// </summary>
    private sealed class ScriptedChatCompletionService(string[] script) : IChatCompletionService
    {
        private int _callIndex;

        public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

        private string NextContent()
        {
            var index = Math.Min(_callIndex++, script.Length - 1);
            return script[index];
        }

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ChatMessageContent> result = new[] { new ChatMessageContent(AuthorRole.Assistant, NextContent()) };
            return Task.FromResult(result);
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, NextContent());
            await Task.CompletedTask;
        }
    }
}
