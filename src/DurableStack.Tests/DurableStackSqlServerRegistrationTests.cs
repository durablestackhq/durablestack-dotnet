using DurableStack.AspNetCore.DependencyInjection;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DurableStack.Tests;

public sealed class DurableStackSqlServerRegistrationTests
{
    [Fact]
    public void AddDurableStackSqlServer_with_connection_string_sets_provider_to_sql_server()
    {
        var services = new ServiceCollection();

        services.AddDurableStackSqlServer("Server=localhost;Database=durable_stack;User Id=sa;Password=Password123!;TrustServerCertificate=true");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DurableStackOptions>();

        Assert.Equal(DurableStackStorageProvider.SqlServer, options.StorageProvider);
        Assert.False(string.IsNullOrWhiteSpace(options.SqlServer.ConnectionString));
        Assert.IsType<DurableStack.SqlServer.Storage.SqlServerJobStore>(provider.GetRequiredService<IDurableJobStore>());
    }

    [Fact]
    public void AddDurableStackSqlServer_with_configuration_uses_connection_strings_durable_stack()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DurableStack"] = "Server=localhost;Database=durable_stack;User Id=sa;Password=Password123!;TrustServerCertificate=true",
            })
            .Build();

        services.AddDurableStackSqlServer(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DurableStackOptions>();

        Assert.Equal(DurableStackStorageProvider.SqlServer, options.StorageProvider);
        Assert.False(string.IsNullOrWhiteSpace(options.SqlServer.ConnectionString));
    }
}
