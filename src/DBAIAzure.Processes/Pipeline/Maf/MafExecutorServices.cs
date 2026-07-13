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

/// <summary>
/// Resolves a service from a <paramref name="primary"/> provider first, then a <paramref name="fallback"/>
/// (spec-019). Lets an orchestrator hand a workflow factory its own per-run dependencies (progress sink,
/// artifact reader) while falling back to a broader container (the SK kernel's service provider) for the
/// board-write infrastructure — a single lookup surface without merging two containers.
/// </summary>
public sealed class CompositeServiceProvider : IServiceProvider
{
    private readonly IServiceProvider _primary;
    private readonly IServiceProvider _fallback;

    /// <summary>Creates a provider that prefers <paramref name="primary"/> and falls back to <paramref name="fallback"/>.</summary>
    public CompositeServiceProvider(IServiceProvider primary, IServiceProvider fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType) => _primary.GetService(serviceType) ?? _fallback.GetService(serviceType);
}
