using XDrishti.Execution.Host;

// Container health probe: `dotnet XDrishti.Execution.dll --healthcheck` (chiseled images have no shell or curl).
if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    return await ExecHealthProbe.RunAsync();
}

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true, reloadOnChange: false); // Database__Password
builder.Services.AddExecution(builder.Configuration).AddExecutionRecord(builder.Configuration);

await builder.Build().RunAsync();
return 0;
