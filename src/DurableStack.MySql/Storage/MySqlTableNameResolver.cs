using System;
using System.Linq;
using DurableStack.Core.Options;

namespace DurableStack.MySql.Storage;

internal static class MySqlTableNameResolver
{
    private const int MaxPrefixLength = 16;
    private const string JobsBase = "durable_stack_jobs";
    private const string RunsBase = "durable_stack_job_runs";
    private const string LocksBase = "durable_stack_job_locks";

    public static string Jobs(DurableStackOptions options)
    {
        return Build(options.DatabaseTablePrefix, JobsBase);
    }

    public static string Runs(DurableStackOptions options)
    {
        return Build(options.DatabaseTablePrefix, RunsBase);
    }

    public static string Locks(DurableStackOptions options)
    {
        return Build(options.DatabaseTablePrefix, LocksBase);
    }

    private static string Build(string? prefix, string baseName)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return baseName;
        }

        var normalizedPrefix = prefix.Trim();

        if (normalizedPrefix.Length > MaxPrefixLength)
        {
            throw new ArgumentException(
                $"DatabaseTablePrefix may be at most {MaxPrefixLength} characters for MySQL providers.",
                nameof(prefix));
        }

        if (!normalizedPrefix.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            throw new ArgumentException(
                "DatabaseTablePrefix may only contain letters, digits, and underscores for MySQL providers (semicolons and other punctuation are not allowed).",
                nameof(prefix));
        }

        return $"{normalizedPrefix}{baseName}";
    }
}
