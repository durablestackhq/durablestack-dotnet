using DurableStack.Core.Options;
using DurableStack.Postgres.Storage;
using DurableStack.SqlServer.Storage;

namespace DurableStack.Tests;

public sealed class TablePrefixValidationTests
{
    [Fact]
    public void Postgres_prefix_rejects_semicolon()
    {
        var options = new DurableStackOptions
        {
            DatabaseTablePrefix = "acme_;",
            Postgres = { ConnectionString = "Host=localhost;Database=durable_stack;Username=postgres;Password=postgres" },
        };

        var ex = Assert.Throws<ArgumentException>(() => _ = new PostgresJobStore(options));
        Assert.Contains("letters, digits, and underscores", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlServer_prefix_rejects_semicolon()
    {
        var options = new DurableStackOptions
        {
            DatabaseTablePrefix = "Acme_;",
            SqlServer = { ConnectionString = "Server=localhost;Database=durable_stack;User Id=sa;Password=Password123!;TrustServerCertificate=true" },
        };

        var ex = Assert.Throws<ArgumentException>(() => _ = new SqlServerJobStore(options));
        Assert.Contains("letters, digits, and underscores", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Postgres_prefix_rejects_length_over_16()
    {
        var options = new DurableStackOptions
        {
            DatabaseTablePrefix = "abcdefghijklmnopq",
            Postgres = { ConnectionString = "Host=localhost;Database=durable_stack;Username=postgres;Password=postgres" },
        };

        var ex = Assert.Throws<ArgumentException>(() => _ = new PostgresJobStore(options));
        Assert.Contains("at most 16 characters", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlServer_prefix_rejects_length_over_16()
    {
        var options = new DurableStackOptions
        {
            DatabaseTablePrefix = "ABCDEFGHIJKLMNOPQ",
            SqlServer = { ConnectionString = "Server=localhost;Database=durable_stack;User Id=sa;Password=Password123!;TrustServerCertificate=true" },
        };

        var ex = Assert.Throws<ArgumentException>(() => _ = new SqlServerJobStore(options));
        Assert.Contains("at most 16 characters", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Prefix_length_16_is_allowed()
    {
        var options = new DurableStackOptions
        {
            DatabaseTablePrefix = "abcdefghijklmnop",
            Postgres = { ConnectionString = "Host=localhost;Database=durable_stack;Username=postgres;Password=postgres" },
            SqlServer = { ConnectionString = "Server=localhost;Database=durable_stack;User Id=sa;Password=Password123!;TrustServerCertificate=true" },
        };

        var postgresStore = new PostgresJobStore(options);
        var sqlServerStore = new SqlServerJobStore(options);

        Assert.NotNull(postgresStore);
        Assert.NotNull(sqlServerStore);
    }
}
