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

    public static Task<HttpResponseMessage> StepUpAsync(this HttpClient client, string? password, CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(new Uri("/api/auth/step-up", UriKind.Relative), new { password }, cancellationToken);

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string path, object? body, CancellationToken cancellationToken) =>
        client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, cancellationToken);

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string path, object? body, CancellationToken cancellationToken) =>
        client.PutAsJsonAsync(new Uri(path, UriKind.Relative), body, cancellationToken);

    public static Task<HttpResponseMessage> DeleteAsync(this HttpClient client, string path, CancellationToken cancellationToken) =>
        client.DeleteAsync(new Uri(path, UriKind.Relative), cancellationToken);

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

    /// <summary>The field errors of an accounts.invalid response: field name to messages.</summary>
    public static async Task<Dictionary<string, string[]>> ReadFieldErrorsAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var errors = new Dictionary<string, string[]>();
        if (document.RootElement.TryGetProperty("errors", out var element))
        {
            foreach (var property in element.EnumerateObject())
            {
                errors[property.Name] = [.. property.Value.EnumerateArray().Select(v => v.GetString()!)];
            }
        }

        return errors;
    }

    public static async Task<T> WithDbAsync<T>(this ApiFactory factory, Func<XDrishtiDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>());
    }
}
