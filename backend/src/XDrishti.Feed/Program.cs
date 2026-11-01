using XDrishti.Feed;

// Container health probe: `dotnet XDrishti.Feed.dll --healthcheck` (chiseled images have no shell or curl).
if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    return await HealthProbe.RunAsync();
}

// Owner-run probe of the expired-options backfill (P14-03 criterion 9): `dotnet XDrishti.Feed.dll --probe-backfill` makes at most five requests and prints what Dhan returned; it stores nothing and starts no loop.
if (args.Contains("--probe-backfill", StringComparer.Ordinal))
{
    return await BackfillProbeCommand.RunAsync(args);
}

var builder = Host.CreateApplicationBuilder(args);
FeedHost.Configure(builder);
builder.Services.AddFeedService();

await builder.Build().RunAsync();
return 0;
