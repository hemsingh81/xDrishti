namespace XDrishti.DhanTrial;

/// <summary>Builds the only HTTP pipeline the tool uses: allow-list guard on top, redirects off (a redirect must never carry credentials elsewhere).</summary>
internal static class GuardedHttp
{
    public static SocketsHttpHandler CreateHandler() => new() { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) };

    public static HttpClient CreateClient(HttpMessageHandler inner) => new(new ReadOnlyGuardHandler(inner), disposeHandler: false) { Timeout = TimeSpan.FromMinutes(3) };
}
