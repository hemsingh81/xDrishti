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
        builder.Services.AddSingleton<GetReportTool>();
        builder.Services.AddSingleton<ListRunsTool>();
        builder.Services.AddSingleton<GetPlanTool>();
        builder.Services.AddSingleton<GetCellTool>();
        builder.Services.AddSingleton<GetPortfolioTool>();
        builder.Services.AddSingleton<GetBasketTool>();
        builder.Services.AddSingleton<GetInstrumentTool>();
        builder.Services.AddSingleton<SearchNotesTool>();
        builder.Services.AddSingleton<GetLearningTool>();
        builder.Services.AddSingleton<GetSystemStatusTool>();
        builder.Services.AddSingleton<CalcTool>();
        AddTool<ListTablesTool>(builder.Services, "List the readable views");
        AddTool<DescribeTableTool>(builder.Services, "Describe a view");
        AddTool<RunSqlTool>(builder.Services, "Run a read-only query");
        AddTool<LabValidateTool>(builder.Services, "Validate a strategy file");
        AddTool<GetReportTool>(builder.Services, "Report engine figures");
        AddTool<ListRunsTool>(builder.Services, "List stored runs");
        AddTool<GetPlanTool>(builder.Services, "Daily plan");
        AddTool<GetCellTool>(builder.Services, "Cell statistics");
        AddTool<GetPortfolioTool>(builder.Services, "Portfolio figures");
        AddTool<GetBasketTool>(builder.Services, "Basket members");
        AddTool<GetInstrumentTool>(builder.Services, "Instrument facts");
        AddTool<SearchNotesTool>(builder.Services, "Search notes");
        AddTool<GetLearningTool>(builder.Services, "Learning records");
        AddTool<GetSystemStatusTool>(builder.Services, "System status");
        AddTool<CalcTool>(builder.Services, "Exact arithmetic");

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
