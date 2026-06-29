// Supplies the aggregated LLM telemetry for a run so it can be written back to ADO work item fields.
using DBAIAzure.Core.Models.AdoTelemetry;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Reads a run's recorded execution events and folds them into a <see cref="RunTelemetryAggregate"/>.
/// Implemented over the persisted <c>WorkflowExecutionEvents</c>; a fake is used in unit tests so the
/// write-back logic can be exercised without a database.
/// </summary>
public interface IRunTelemetrySource
{
    /// <summary>
    /// Returns the aggregated telemetry for <paramref name="runId"/>. When the run produced no LLM
    /// events, returns an empty aggregate (only the run id is populated) rather than null.
    /// </summary>
    Task<RunTelemetryAggregate> GetAggregateAsync(string runId, CancellationToken cancellationToken = default);
}
