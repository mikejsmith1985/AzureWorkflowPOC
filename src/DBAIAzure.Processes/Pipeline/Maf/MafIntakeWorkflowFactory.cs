// Builds the intake pipeline as a MAF Workflow (spec-019 T019) — the GA replacement for the SK
// IntakePipelineBuilder. Executors route by directed SendMessageAsync over the graph edges.
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Executors;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace DBAIAzure.Processes.Pipeline.Maf;

/// <summary>
/// Assembles the ticket-intake pipeline as a MAF <see cref="Workflow"/>: the executors
/// (<see cref="MafExecutorIds.Intake"/> → <see cref="MafExecutorIds.Validation"/> → ready path
/// <see cref="MafExecutorIds.Estimation"/> → <see cref="MafExecutorIds.Action"/>; not-ready path
/// <see cref="MafExecutorIds.GapAnalysis"/> → <see cref="MafExecutorIds.IntakeHitl"/>) wired with
/// conditional edges for the ready/not-ready branch. All model access flows through the provided
/// <see cref="IChatClient"/> (no provider-specific type in the graph). Replaces
/// <c>IntakePipelineBuilder</c> (SK <c>ProcessBuilder</c>).
/// </summary>
public static class MafIntakeWorkflowFactory
{
    /// <summary>
    /// Builds the intake workflow. <paramref name="chatClient"/> is the provider-neutral model client
    /// every LLM executor uses; <paramref name="services"/> supplies any additional per-executor
    /// dependencies (work-tracker adapter, repositories) resolved at build time.
    /// </summary>
    /// <returns>A runnable <see cref="Workflow"/> whose start executor accepts the initial ticket.</returns>
    public static Workflow Build(IChatClient chatClient, IServiceProvider? services = null)
    {
        var reporter = services?.GetService(typeof(IProgressReporter)) as IProgressReporter;

        var intake = new IntakeExecutor(chatClient, reporter).BindExecutor();
        var validation = new ValidationExecutor(chatClient, reporter).BindExecutor();
        var estimation = new EstimationExecutor(chatClient, reporter).BindExecutor();
        var action = new ActionExecutor(reporter).BindExecutor();
        var gapAnalysis = new GapAnalysisExecutor(chatClient, reporter).BindExecutor();

        // The clarification gate: a request carrying the ticket (with questions), resolved by the host with
        // the answered ticket (HumanAnswer set, clarification round incremented). The run suspends here
        // (RequestInfoEvent) until the host responds; the answered ticket then re-enters validation, forming
        // the clarification loop (ValidationExecutor blocks after the max round, ending the loop).
        var hitl = RequestPort.Create<TicketState, TicketState>(MafExecutorIds.IntakeHitl).BindAsExecutor(allowWrappedRequests: false);

        // Edges mirror the SK graph. Executors broadcast; the ready/not-ready branch is disambiguated by
        // conditional edges on the emitted ticket: the ready path carries a ticket with no clarifying
        // questions, the not-ready path carries the questions the validation executor attached.
        return new WorkflowBuilder(intake)
            .AddEdge(intake, validation)
            .AddEdge(validation, estimation, (TicketState ticket) => IsReadyTicket(ticket))       // ready path (no questions)
            .AddEdge(validation, gapAnalysis, (TicketState ticket) => IsNotReadyTicket(ticket))   // not-ready path (has questions)
            .AddEdge(estimation, action)
            .AddEdge(gapAnalysis, hitl)
            .AddEdge(hitl, validation)          // resume: the answered ticket re-enters validation (loop)
            .WithOutputFrom(action, validation) // Action (ready) + Validation (blocked) yield the final ticket
            .Build(validateOrphans: true);
    }

    /// <summary>Ready branch: the validation executor forwarded the ticket with no clarifying questions.</summary>
    private static bool IsReadyTicket(TicketState ticket) => ticket.ClarifyingQuestions.Count == 0;

    /// <summary>Not-ready branch: the validation executor attached clarifying questions before forwarding.</summary>
    private static bool IsNotReadyTicket(TicketState ticket) => ticket.ClarifyingQuestions.Count > 0;
}
