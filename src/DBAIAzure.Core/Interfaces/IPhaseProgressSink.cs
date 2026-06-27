// Lets phase-handler steps report their resulting state back to the orchestrator (mirrors IProgressReporter).
using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Receives the state each phase-handler step produces, so the orchestrator can capture the final
/// outcome after the SK process terminates (the framework's event payloads are internal to the
/// process). Optional in the kernel — when absent, steps simply skip reporting. This is the
/// phase-handler analogue of <c>IProgressReporter</c> in the ticket pipeline.
/// </summary>
public interface IPhaseProgressSink
{
    /// <summary>Records the latest state emitted by a step.</summary>
    void Report(PhaseHandlerState state);
}
