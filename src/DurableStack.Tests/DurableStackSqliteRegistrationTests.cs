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
    public void AddDurableStackSqlite_with_configuration_uses_connection_strings_durable_stack()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DurableStack"] = "Data Source=durable_stack.db",
            })
            .Build();

        services.AddDurableStackSqlite(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DurableStackOptions>();

        Assert.Equal(DurableStackStorageProvider.Sqlite, options.StorageProvider);
        Assert.False(string.IsNullOrWhiteSpace(options.Sqlite.ConnectionString));
    }

    [Fact]
    public void AddDurableStackSqlite_with_connection_string_name_uses_named_connection_string()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:acmewidgets_prod"] = "Data Source=durable_stack.db",
            })
            .Build();

        services.AddDurableStackSqlite(configuration, options =>
        {
            options.ConnectionStringName = "acmewidgets_prod";
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DurableStackOptions>();

        Assert.Equal("acmewidgets_prod", options.ConnectionStringName);
        Assert.Equal("Data Source=durable_stack.db", options.Sqlite.ConnectionString);
    }
}
