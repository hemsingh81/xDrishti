namespace XDrishti.Mcp;

/// <summary>
/// The container health check: <c>dotnet XDrishti.Mcp.dll --healthcheck</c> (chiseled images have no shell or curl). Exit 0 when the host's own <c>/health</c> answers 200 (the read login can run a query), 1 otherwise. It asks over the loopback address with the one host name the
/// host accepts, and builds none of the host.
/// </summary>
internal static class HealthProbe
{
    public static async Task<int> RunAsync(string? address = null)
    {
        try
        {
            var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") is { Length: > 0 } ports ? ports.Split(';')[0] : "8080";
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var request = new HttpRequestMessage(HttpMethod.Get, address ?? $"http://127.0.0.1:{port}/health");
            request.Headers.Host = McpHost.ServiceName;
            using var response = await client.SendAsync(request);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
#pragma warning disable CA1031 // any failure means the host is not healthy; the exit code is the answer
        catch (Exception)
#pragma warning restore CA1031
        {
            return 1;
        }
    }
}
