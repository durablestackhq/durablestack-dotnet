using DurableStack.Hosting.DependencyInjection;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DurableStack.Tests;

public sealed class DurableStackSqliteRegistrationTests
{
    [Fact]
    public void AddDurableStackSqlite_with_connection_string_sets_provider_to_sqlite()
    {
        var services = new ServiceCollection();

        services.AddDurableStackSqlite("Data Source=durable_stack.db");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DurableStackOptions>();

        Assert.Equal(DurableStackStorageProvider.Sqlite, options.StorageProvider);
        Assert.False(string.IsNullOrWhiteSpace(options.Sqlite.ConnectionString));
        Assert.IsType<DurableStack.Sqlite.Storage.SqliteJobStore>(provider.GetRequiredService<IDurableJobStore>());
    }

    [Fact]
    public void AddDurableStackSqlite_with_configuration_uses_durable_stack_section_connection_string()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DurableStack:Sqlite:ConnectionString"] = "Data Source=durable_stack.db",
            })
            .Build();

        services.AddDurableStackSqlite(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DurableStackOptions>();

        Assert.Equal(DurableStackStorageProvider.Sqlite, options.StorageProvider);
        Assert.False(string.IsNullOrWhiteSpace(options.Sqlite.ConnectionString));
    }

    [Fact]
    public void AddDurableStackSqlite_with_options_connection_string_sets_provider_to_sqlite()
    {
        var services = new ServiceCollection();
        services.AddDurableStackSqlite(configure: options =>
        {
            options.Sqlite.ConnectionString = "Data Source=durable_stack.db";
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DurableStackOptions>();

        Assert.Equal("Data Source=durable_stack.db", options.Sqlite.ConnectionString);
        Assert.Equal(DurableStackStorageProvider.Sqlite, options.StorageProvider);
    }
}
