using System;

namespace DurableStack.Core.Options;

public sealed class DurableStackRetentionOptions
{
    private const double InMemoryDefaultRunRetentionSeconds = 60 * 60;
    private const double DurableStoreDefaultRunRetentionSeconds = 24 * 60 * 60;
    private const double DefaultSweepIntervalSeconds = 5 * 60;
    private const int DefaultDeleteBatchSize = 1000;

    public bool Enabled { get; set; } = true;

    public double? RunRetentionSeconds { get; set; }

    public double SweepIntervalSeconds { get; set; } = DefaultSweepIntervalSeconds;

    public int DeleteBatchSize { get; set; } = DefaultDeleteBatchSize;

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

    public TimeSpan GetEffectiveSweepInterval()
    {
        var seconds = SweepIntervalSeconds;
        if (seconds <= 0)
        {
            seconds = DefaultSweepIntervalSeconds;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    public int GetEffectiveDeleteBatchSize()
    {
        return DeleteBatchSize > 0 ? DeleteBatchSize : DefaultDeleteBatchSize;
    }
}
