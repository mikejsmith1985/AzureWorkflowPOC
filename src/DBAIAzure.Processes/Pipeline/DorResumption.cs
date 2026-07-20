// The ways a suspended DoR conversation can be resumed (spec-021 US2/US3). A human reply continues the
// conversation; an escalation moves it to the escalation tier; a manual exit ends it. All three flow through the
// same HITL RequestPort response so the graph routes on the resumed state.
namespace DBAIAzure.Processes.Pipeline;

/// <summary>How a suspended run's human gate is being answered.</summary>
public abstract record DorResumption;

/// <summary>A human replied in the conversation thread.</summary>
public sealed record HumanReplyResumption(string Reply) : DorResumption;

/// <summary>The primary SLA breached — escalate to the escalation channel/tier.</summary>
public sealed record EscalateResumption : DorResumption;

/// <summary>Limits are exhausted — end the run with a clean manual handoff.</summary>
public sealed record ManualExitResumption(string Reason) : DorResumption;
