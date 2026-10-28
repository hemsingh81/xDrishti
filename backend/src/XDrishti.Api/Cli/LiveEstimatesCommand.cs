using System.Globalization;
using System.Text;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.LiveTrading;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd live estimates</c> and <c>xd live waterfall</c> (spec P11-02): <c>estimates</c> computes and stores the slippage per liquidity bucket and the capacity score of a week (one snapshot per week and key; a second run writes nothing) and prints them; <c>waterfall</c>
/// prints the waterfall of one round trip. Both read stored rows only: nothing is read from or sent to Dhan, nothing is applied to the simulator, and nothing is placed.
/// </summary>
internal static class LiveEstimatesCommand
{
    public const string Usage = """
        usage:
          xd live estimates [--week YYYY-MM-DD]
            computes and stores the slippage per liquidity bucket and the capacity score of the week (a Monday; default this week), then prints them
          xd live waterfall --entry ID
            prints the waterfall of one round trip (plan, entry slippage, exit difference, actual, charges)
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (command, week, entry, errors) = Parse(args);
        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            if (command == "waterfall")
            {
                var w = await sp.GetRequiredService<IQueryHandler<LiveWaterfallQuery, Result<LiveWaterfallView>>>().HandleAsync(new LiveWaterfallQuery(entry), cancellationToken);
                if (w.IsFailure)
                {
                    await console.Error.WriteLineAsync(SimulateOptions.Show(w.Error.Code + ": " + w.Error.Message));
                    return XdCommand.Refused;
                }

                await console.Out.WriteAsync(RenderWaterfall(w.Value));
                return XdCommand.Done;
            }

            var computed = await sp.GetRequiredService<ICommandHandler<ComputeLiveEstimatesCommand, LiveEstimatesOutcome>>().HandleAsync(new ComputeLiveEstimatesCommand(week), cancellationToken);
            if (computed.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(computed.Error.Code + ": " + computed.Error.Message));
                return XdCommand.Refused;
            }

            await console.Out.WriteLineAsync("estimates: " + SimulateOptions.Show(computed.Value.Summary));
            var slippage = await sp.GetRequiredService<IQueryHandler<LiveSlippageQuery, Result<LiveSlippageView>>>().HandleAsync(new LiveSlippageQuery(computed.Value.WeekStart), cancellationToken);
            var capacity = await sp.GetRequiredService<IQueryHandler<LiveCapacityQuery, Result<LiveCapacityView>>>().HandleAsync(new LiveCapacityQuery(computed.Value.WeekStart), cancellationToken);
            if (slippage.IsFailure || capacity.IsFailure)
            {
                await console.Error.WriteLineAsync("failed: the stored snapshot could not be read");
                return XdCommand.Failed;
            }

            await console.Out.WriteAsync(RenderEstimates(slippage.Value, capacity.Value));
            return XdCommand.Done;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await console.Error.WriteLineAsync("cancelled");
            return XdCommand.Cancelled;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            await console.Error.WriteLineAsync("failed: " + SimulateCommand.Describe(failure));
            return XdCommand.Failed;
        }
    }

    internal static string RenderEstimates(LiveSlippageView slippage, LiveCapacityView capacity)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"slippage, basis points, positive is worse ({slippage.State}), bucket edges (crore) {string.Join(", ", slippage.Evidence.BucketEdgesCr.Select(e => e.ToString(CultureInfo.InvariantCulture)))}");
        foreach (var r in slippage.Rows)
        {
            var figure = r.Mean.Number is { } m ? string.Create(CultureInfo.InvariantCulture, $"mean {m:0.00} ({Show(r.MeanLo)} to {Show(r.MeanHi)}) median {Show(r.Median)}") : r.Mean.State + ": " + r.Mean.Reason;
            text.AppendLine(SimulateOptions.Show(string.Create(CultureInfo.InvariantCulture, $"  {r.Bucket} {r.Side} {r.Style} {r.ReferenceKind} n = {r.N} {figure}")));
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"capacity, share of orders filled in full ({capacity.State})");
        foreach (var r in capacity.Rows)
        {
            var figure = r.Score.Number is { } s ? string.Create(CultureInfo.InvariantCulture, $"{s:0.0000} ({Show(r.Lo)} to {Show(r.Hi)})") : r.Score.State + ": " + r.Score.Reason;
            text.AppendLine(SimulateOptions.Show(string.Create(CultureInfo.InvariantCulture, $"  {r.Scope} {r.Key} counted {r.NOrders} full {r.NFull} partial {r.NPartial} rejected {r.NRejected} queue miss {r.NQueueMiss} price miss {r.NPriceMiss} unclassified {r.NUnclassified} {figure}")));
        }

        return text.ToString();
    }

    internal static string RenderWaterfall(LiveWaterfallView view)
    {
        var text = new StringBuilder();
        text.AppendLine(SimulateOptions.Show(string.Create(CultureInfo.InvariantCulture, $"waterfall {view.Symbol} {view.Date:yyyy-MM-dd} ({view.State}, {view.Basis}, balanced {view.Balanced})")));
        foreach (var r in view.Rows)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {r.Label,-38} {r.Op,-6} R {Show(r.R),8}  rupees {r.Rupees,12:0.00}"));
        }

        foreach (var note in view.Notes)
        {
            text.AppendLine("  note: " + SimulateOptions.Show(note));
        }

        return text.ToString();
    }

    private static string Show(XDrishti.Application.Reporting.MetricView m) => m.Number is { } n ? n.ToString("0.0000", CultureInfo.InvariantCulture) : m.State;

    private static (string Command, DateOnly? Week, long Entry, List<string> Errors) Parse(IReadOnlyList<string> args)
    {
        var errors = new List<string>();
        DateOnly? week = null;
        long entry = 0;
        var command = args.Count > 0 ? args[0] : string.Empty;
        if (command is not ("estimates" or "waterfall"))
        {
            errors.Add("a command is required: estimates or waterfall");
            return (command, null, 0, errors);
        }

        for (var i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--week" when command == "estimates" && i + 1 < args.Count:
                    if (DateOnly.TryParseExact(args[++i], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    {
                        week = d;
                    }
                    else
                    {
                        errors.Add("--week is YYYY-MM-DD");
                    }

                    break;
                case "--entry" when command == "waterfall" && i + 1 < args.Count:
                    if (!long.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out entry) || entry <= 0)
                    {
                        errors.Add("--entry is an execution id");
                    }

                    break;
                default:
                    errors.Add($"unknown or incomplete option '{SimulateOptions.Show(args[i])}'");
                    break;
            }
        }

        if (command == "waterfall" && entry <= 0 && errors.Count == 0)
        {
            errors.Add("--entry is required");
        }

        return (command, week, entry, errors);
    }
}
