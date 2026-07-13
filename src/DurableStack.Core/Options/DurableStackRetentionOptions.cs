using System;

namespace DurableStack.Core.Options;

/// <summary>
/// Settings for the retention sweep that deletes completed (succeeded or failed) runs once
/// they age past the retention window. The sweep runs on the worker's poll loop; pending
/// and executing runs are never deleted.
/// </summary>
public sealed class DurableStackRetentionOptions
{
    private const double InMemoryDefaultRunRetentionSeconds = 60 * 60;
    private const double DurableStoreDefaultRunRetentionSeconds = 24 * 60 * 60;
    private const double DefaultSweepIntervalSeconds = 5 * 60;
    private const int DefaultDeleteBatchSize = 1000;

    /// <summary>
    /// Whether completed runs are deleted after the retention window. On by default; turn
    /// off to keep full run history (history then grows without bound).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How long completed runs are kept, in seconds. Null (the default) or a non-positive
    /// value uses the provider default: 1 hour for the in-memory store, 24 hours for
    /// database stores.
    /// </summary>
    public double? RunRetentionSeconds { get; set; }

    /// <summary>
    /// How often the worker runs a retention sweep, in seconds. Defaults to 300 (5 minutes);
    /// zero or negative values fall back to the default when the interval is applied.
    /// </summary>
    public double SweepIntervalSeconds { get; set; } = DefaultSweepIntervalSeconds;

    /// <summary>
    /// Maximum number of runs deleted per sweep, bounding transaction size on busy systems.
    /// Defaults to 1000; zero or negative values fall back to the default when applied.
    /// </summary>
    public int DeleteBatchSize { get; set; } = DefaultDeleteBatchSize;

    /// <summary>
    /// Returns the retention window actually applied: <see cref="RunRetentionSeconds"/> when
    /// positive, otherwise 1 hour for the in-memory provider or 24 hours for database
    /// providers.
    /// </summary>
    /// <param name="storageProvider">The configured storage provider, which selects the fallback default.</param>
    public TimeSpan GetEffectiveRunRetention(DurableStackStorageProvider storageProvider)
    {
        var defaultSeconds = storageProvider == DurableStackStorageProvider.InMemory
            ? InMemoryDefaultRunRetentionSeconds
            : DurableStoreDefaultRunRetentionSeconds;

        var seconds = RunRetentionSeconds ?? defaultSeconds;
        if (seconds <= 0)
        {
            seconds = defaultSeconds;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Returns the sweep interval actually applied: <see cref="SweepIntervalSeconds"/> when
    /// positive, otherwise the 5-minute default.
    /// </summary>
    public TimeSpan GetEffectiveSweepInterval()
    {
        var seconds = SweepIntervalSeconds;
        if (seconds <= 0)
        {
            seconds = DefaultSweepIntervalSeconds;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Returns the delete batch size actually applied: <see cref="DeleteBatchSize"/> when
    /// positive, otherwise the default of 1000.
    /// </summary>
    public int GetEffectiveDeleteBatchSize()
    {
        return DeleteBatchSize > 0 ? DeleteBatchSize : DefaultDeleteBatchSize;
    }
}
