// A tiny service bag the orchestrators use to hand per-run dependencies (progress sink, artifact reader,
// repositories) to the workflow factories when building a MAF workflow (spec-019 T022). Keeps the
// factories DI-agnostic without pulling a full container into DBAIAzure.Processes.
namespace DBAIAzure.Processes.Pipeline.Maf;

/// <summary>
/// A minimal <see cref="IServiceProvider"/> populated per run so a workflow factory can resolve the
/// executor dependencies the orchestrator already holds (e.g. the run-bound progress reporter). Only the
/// registered types resolve; everything else returns null, matching the factories' optional-lookup style.
/// </summary>
public sealed class MafExecutorServices : IServiceProvider
{
    private readonly Dictionary<Type, object> _servicesByType = new();

    /// <summary>Registers <paramref name="instance"/> as the resolution for <typeparamref name="TService"/>.</summary>
    public MafExecutorServices Add<TService>(TService instance) where TService : class
    {
        _servicesByType[typeof(TService)] = instance;
        return this;
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType) =>
        _servicesByType.TryGetValue(serviceType, out var service) ? service : null;
}
