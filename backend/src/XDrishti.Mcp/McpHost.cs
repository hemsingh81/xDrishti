using Microsoft.Extensions.AI;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using XDrishti.Hosting;
using XDrishti.Infrastructure.Ai.ReadOnly;

namespace XDrishti.Mcp;

/// <summary>The wiring of the <c>xd-mcp</c> host, in one place so that the host, the tests and the health probe build the same thing. The host registers the read side of the infrastructure only: no <c>DbContext</c>, no command handler, no store.</summary>
internal static class McpHost
{
    public const string ServiceName = "xd-mcp";

    public static WebApplicationBuilder Configure(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddXDrishtiDefaults(ServiceName);
        builder.Services.AddOptions<McpOptions>()
            .Bind(builder.Configuration.GetSection(McpOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAiReadOnly(builder.Configuration);
        builder.Services.AddSingleton<ToolRunner>();
        builder.Services.AddSingleton<ListTablesTool>();
        builder.Services.AddSingleton<DescribeTableTool>();
        builder.Services.AddSingleton<RunSqlTool>();
        builder.Services.AddSingleton<LabValidateTool>();
        AddTool<ListTablesTool>(builder.Services, "List the readable views");
        AddTool<DescribeTableTool>(builder.Services, "Describe a view");
        AddTool<RunSqlTool>(builder.Services, "Run a read-only query");
        AddTool<LabValidateTool>(builder.Services, "Validate a strategy file");

        // Stateless Streamable HTTP: no session to hijack, nothing kept between calls (a token per client instead).
        builder.Services.AddMcpServer().WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless);
        return builder;
    }

    public static WebApplication Use(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseMiddleware<McpGateMiddleware>();
        app.MapGet("/health", async (IServiceProvider services, CancellationToken cancellationToken) =>
            await services.CanReadAsync(cancellationToken) ? (IResult)TypedResults.Ok("ok") : TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable));
        app.MapMcp("/mcp");
        return app;
    }

    /// <summary>Registers the tool with the read-only annotations the contract requires: read-only, not destructive, idempotent, closed world.</summary>
    private static void AddTool<TTool>(IServiceCollection services, string title)
        where TTool : AIFunction =>
        services.AddSingleton(sp => McpServerTool.Create(sp.GetRequiredService<TTool>(), new McpServerToolCreateOptions { Title = title, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false }));
}
