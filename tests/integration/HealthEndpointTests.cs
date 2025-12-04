using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace IntegrationTests;

public class HealthEndpointTests
{
    [Fact]
    public async Task GetHealth_ReturnsOk()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        var app = builder.Build();

        app.MapGet("/health", () => Results.Ok());

        await app.StartAsync();
        var client = app.GetTestServer().CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
