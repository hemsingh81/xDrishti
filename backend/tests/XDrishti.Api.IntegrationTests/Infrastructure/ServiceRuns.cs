using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Platform.Services;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>Runs a service of the framework now, as the Services screen's Run now does, and waits for the run to end.</summary>
internal static class ServiceRuns
{
    public static async Task<ServiceRunView> RunNowAsync(this ApiFactory factory, HttpClient client, string serviceId, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync($"/api/services/{serviceId}/run", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var started = (await response.Content.ReadFromJsonAsync<ServiceRunView>(cancellationToken))!;
        await factory.Services.GetRequiredService<ServiceRunner>().WhenIdleAsync();
        using var runs = await client.GetAsync($"/api/services/{serviceId}/runs", cancellationToken);
        return (await runs.ReadAsync<ServiceRunPage>(cancellationToken)).Items.Single(r => r.Id == started.Id);
    }
}
