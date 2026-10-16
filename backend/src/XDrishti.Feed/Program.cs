using XDrishti.Feed;

// Container health probe: `dotnet XDrishti.Feed.dll --healthcheck` (chiseled images have no shell or curl).
if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    return await HealthProbe.RunAsync();
}

var builder = Host.CreateApplicationBuilder(args);
FeedHost.Configure(builder);
builder.Services.AddFeedService();

await builder.Build().RunAsync();
return 0;
