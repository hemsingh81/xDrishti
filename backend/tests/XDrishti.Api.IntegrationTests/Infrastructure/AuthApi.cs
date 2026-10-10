using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>Small helpers shared by the auth tests.</summary>
internal static class AuthApi
{
    public static Task<HttpResponseMessage> LoginAsync(this HttpClient client, string? email, string? password, CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(new Uri("/api/auth/login", UriKind.Relative), new { email, password }, cancellationToken);

    public static Task<HttpResponseMessage> GetAsync(this HttpClient client, string path, CancellationToken cancellationToken) =>
        client.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);

    public static Task<HttpResponseMessage> PostAsync(this HttpClient client, string path, CancellationToken cancellationToken) =>
        client.PostAsync(new Uri(path, UriKind.Relative), content: null, cancellationToken);

    /// <summary>The stable error code and optional retry hint of a problem+json response.</summary>
    public static async Task<(string? Code, int? RetryAfterSeconds)> ReadProblemAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        var code = root.TryGetProperty("code", out var c) ? c.GetString() : null;
        int? retry = root.TryGetProperty("retryAfterSeconds", out var r) ? r.GetInt32() : null;
        return (code, retry);
    }

    public static async Task<T> WithDbAsync<T>(this ApiFactory factory, Func<XDrishtiDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>());
    }
}
