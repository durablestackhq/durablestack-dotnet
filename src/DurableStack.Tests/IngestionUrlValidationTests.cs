using System;
using System.Net.Http;
using DurableStack.Core.Abstractions;
using DurableStack.Core.Options;
using DurableStack.Hosting.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace DurableStack.Tests;

public sealed class IngestionUrlValidationTests
{
    [Theory]
    [InlineData("https://api.durablestack.com")]
    [InlineData("https://ingest.example.com:8443/base")]
    [InlineData("http://localhost:5000")]
    [InlineData("http://127.0.0.1:8080")]
    public void Accepts_https_and_loopback_http_urls(string baseUrl)
    {
        var service = CreateService(baseUrl);
        Assert.NotNull(service);
    }

    [Theory]
    [InlineData("http://api.durablestack.com")]
    [InlineData("http://ingest.internal.corp")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    public void Rejects_cleartext_and_invalid_urls_at_startup(string baseUrl)
    {
        // The ingestion request carries tenant credentials; refusing cleartext (and
        // malformed URLs) at construction fails the host at startup instead of
        // leaking secrets or looping error logs at first flush.
        Assert.Throws<ArgumentException>(() => CreateService(baseUrl));
    }

    private static IngestionEventSyncHostedService CreateService(string baseUrl)
    {
        var options = new DurableStackOptions();
        options.Eventing.IngestionApiBaseUrl = baseUrl;

        return new IngestionEventSyncHostedService(
            Array.Empty<IDurableStackEventSink>(),
            options,
            new StubHttpClientFactory(),
            NullLogger<IngestionEventSyncHostedService>.Instance);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }
}
