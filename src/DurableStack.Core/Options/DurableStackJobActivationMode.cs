namespace DurableStack.Core.Options;

/// <summary>
/// How job instances are resolved from dependency injection for each execution.
/// </summary>
public enum DurableStackJobActivationMode
{
    /// <summary>
    /// Creates a fresh DI scope for every execution and disposes it when the attempt
    /// finishes, so jobs can depend on scoped services such as EF Core DbContexts. This is
    /// the default.
    /// </summary>
    ScopedPerExecution = 0,

    /// <summary>
    /// Resolves jobs from the application's root provider with no per-execution scope.
    /// Singleton-style jobs only: resolving scoped services fails, and instances are shared
    /// across concurrent executions.
    /// </summary>
    RootProvider = 1,
}
