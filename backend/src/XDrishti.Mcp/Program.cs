namespace XDrishti.Mcp;

/// <summary>
/// Entry point of the read-only tool server. An explicit class rather than top-level statements, so that the host adds no type named <c>Program</c> next to the API's (the test projects that load both assemblies would find the name in two places).
/// </summary>
internal static class McpProgram
{
    public static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        // Container health probe: `dotnet XDrishti.Mcp.dll --healthcheck` (chiseled images have no shell or curl).
        if (args.Contains("--healthcheck", StringComparer.Ordinal))
        {
            return await HealthProbe.RunAsync();
        }

        var builder = WebApplication.CreateBuilder(args);
        McpHost.Configure(builder);
        var app = builder.Build();
        McpHost.Use(app);
        await app.RunAsync();
        return 0;
    }
}
