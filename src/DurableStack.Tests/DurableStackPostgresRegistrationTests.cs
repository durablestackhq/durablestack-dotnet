using DurableStack.Hosting.DependencyInjection;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DurableStack.Tests;

public sealed class DurableStackPostgresRegistrationTests
{
    [Fact]
    public void AddDurableStackPostgres_with_connection_string_sets_provider_to_postgres()
    {
        var services = new ServiceCollection();

        services.AddDurableStackPostgres("Host=localhost;Database=durable_stack;Username=postgres;Password=postgres");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DurableStackOptions>();

        Assert.Equal(DurableStackStorageProvider.Postgres, options.StorageProvider);
        Assert.False(string.IsNullOrWhiteSpace(options.Postgres.ConnectionString));
        Assert.IsType<DurableStack.Postgres.Storage.PostgresJobStore>(provider.GetRequiredService<IDurableJobStore>());
    }

    [Fact]
    public void AddDurableStackPostgres_with_configuration_uses_durable_stack_section_connection_string()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DurableStack:Postgres:ConnectionString"] = "Host=localhost;Database=durable_stack;Username=postgres;Password=postgres",
            })
            .Build();

        services.AddDurableStackPostgres(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DurableStackOptions>();

        Assert.Equal(DurableStackStorageProvider.Postgres, options.StorageProvider);
        Assert.False(string.IsNullOrWhiteSpace(options.Postgres.ConnectionString));
    }

    [Fact]
    public void AddDurableStackPostgres_with_options_connection_string_sets_provider_to_postgres()
    {
        var services = new ServiceCollection();
        services.AddDurableStackPostgres(configure: options =>
        {
            options.Postgres.ConnectionString = "Host=localhost;Database=durable_stack;Username=postgres;Password=postgres";
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DurableStackOptions>();

        Assert.Equal("Host=localhost;Database=durable_stack;Username=postgres;Password=postgres", options.Postgres.ConnectionString);
        Assert.Equal(DurableStackStorageProvider.Postgres, options.StorageProvider);
    }
}
