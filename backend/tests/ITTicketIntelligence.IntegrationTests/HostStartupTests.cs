using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests;

public class HostStartupTests
{
    [Fact]
    public async Task Api_Starts_AndReturnsProblemDetailsForUnknownRoute()
    {
        await using var factory = new WebApplicationFactory<Api.Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/unmapped-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
    }

    [Fact]
    public async Task Worker_StartsAndStops()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var host = Worker.Program.CreateHostBuilder([])
            .UseEnvironment("Testing")
            .Build();

        await host.StartAsync(timeout.Token);
        await host.StopAsync(timeout.Token);
    }
}
