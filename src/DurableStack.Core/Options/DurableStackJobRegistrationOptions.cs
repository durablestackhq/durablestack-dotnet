namespace DurableStack.Core.Options;

/// <summary>
/// Settings controlling how job types are discovered and registered at startup.
/// </summary>
public sealed class DurableStackJobRegistrationOptions
{
    /// <summary>
    /// When true (the default), the calling assembly is scanned for job implementations
    /// and they are registered automatically. Set to false to register every job
    /// explicitly.
    /// </summary>
    public bool AutoDiscoverJobsFromAssembly { get; set; } = true;
}
