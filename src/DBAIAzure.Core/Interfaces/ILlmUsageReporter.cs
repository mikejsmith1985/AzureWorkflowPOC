// Receives the usage of every LLM call so it can be recorded against the current run.
using DBAIAzure.Core.Models.AdoTelemetry;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Sink for per-call LLM usage. The connector calls <see cref="Report"/> after every Messages API
/// response (success or failure); an implementation records it as a run-correlated execution event.
/// Optional on the connector (null → no-op), so the Connectors project keeps no observer dependency.
/// Implementations MUST NOT throw — telemetry is best-effort and must never disrupt the call (FR-010).
/// </summary>
public interface ILlmUsageReporter
{
    /// <summary>Records the usage of one completed (or failed) LLM call. Must not throw.</summary>
    void Report(LlmUsage usage);
}
