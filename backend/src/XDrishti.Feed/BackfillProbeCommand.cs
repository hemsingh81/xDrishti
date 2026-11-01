using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.OptionBackfill;

namespace XDrishti.Feed;

/// <summary>
/// <c>dotnet XDrishti.Feed.dll --probe-backfill</c> (P14-03 criterion 9): the owner runs it by hand, inside the xd-feed container (<c>scripts/xd-probe-backfill.sh</c>), once, before the full run. It builds the host's services (no loop is started), makes the
/// probe's few requests with the Data account's token and prints what Dhan returned. <b>It stores nothing</b>, prints no token, client id or URL, and does not depend on the deployment flag (the probe comes first, before anything is switched on). Exit 0 when it
/// ran, 2 when it could not start.
/// </summary>
internal static class BackfillProbeCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var builder = Host.CreateApplicationBuilder(args);
            FeedHost.Configure(builder);
            using var host = builder.Build();
            await using var scope = host.Services.CreateAsyncScope();
            var probe = scope.ServiceProvider.GetRequiredService<BackfillProbe>();
            using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var targets = await scope.ServiceProvider.GetRequiredService<IBackfillTargets>().LoadAsync(cancel.Token);
            foreach (var line in await probe.RunAsync(targets, scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow(), cancel.Token))
            {
                Console.Out.WriteLine(line);
            }

            return 0;
        }
#pragma warning disable CA1031 // the probe is a command line tool: any failure is a refusal with a short message, never a stack trace that could name the endpoint
        catch (Exception ex)
#pragma warning restore CA1031
        {
            await Console.Error.WriteLineAsync($"The probe could not run ({ex.GetType().Name}).");
            return 2;
        }
    }
}
