using DurableStack.Hosting.DependencyInjection;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DurableStack.Tests;

public sealed class DurableStackMySqlRegistrationTests
{
    [Fact]
    public void AddDurableStackMySql_with_connection_string_sets_provider_to_mysql()
    {
        var services = new ServiceCollection();

        services.AddDurableStackMySql("Server=localhost;Port=3306;Database=durable_stack;User ID=root;Password=postgres;");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DurableStackOptions>();

        Assert.Equal(DurableStackStorageProvider.MySql, options.StorageProvider);
        Assert.False(string.IsNullOrWhiteSpace(options.MySql.ConnectionString));
        Assert.IsType<DurableStack.MySql.Storage.MySqlJobStore>(provider.GetRequiredService<IDurableJobStore>());
    }

    [Fact]
    public void AddDurableStackMySql_with_configuration_uses_connection_strings_durable_stack()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DurableStack"] = "Server=localhost;Port=3306;Database=durable_stack;User ID=root;Password=postgres;",
            })
            .Build();

        services.AddDurableStackMySql(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DurableStackOptions>();

        Assert.Equal(DurableStackStorageProvider.MySql, options.StorageProvider);
        Assert.False(string.IsNullOrWhiteSpace(options.MySql.ConnectionString));
    }

    [Fact]
    public void AddDurableStackMySql_with_connection_string_name_uses_named_connection_string()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:acmewidgets_prod"] = "Server=localhost;Port=3306;Database=durable_stack;User ID=root;Password=postgres;",
            })
            .Build();

        services.AddDurableStackMySql(configuration, options =>
        {
            options.ConnectionStringName = "acmewidgets_prod";
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DurableStackOptions>();

        Assert.Equal("acmewidgets_prod", options.ConnectionStringName);
        Assert.Equal("Server=localhost;Port=3306;Database=durable_stack;User ID=root;Password=postgres;", options.MySql.ConnectionString);
    }
}
