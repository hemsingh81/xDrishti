using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary><c>xd paper sequential</c> (spec CF-09, ADR 0213): prints the sequential failure test of the paper edge series, with the P10 behavioural series beside it. Read only; it changes nothing and is never a pass.</summary>
internal static class PaperSequentialCommand
{
    public const string Usage = """
        usage:
          xd paper sequential [--scope system|strategy] [--strategy ID]
            the sequential failure test of the paper edge series (graded shadow trades that count for edge, current epoch): state (monitoring, on_track, caution or failure), the running sum against the boundaries, trades counted, effective n, the first crossing,
            the correlation used and the settings hash, with the P10 behavioural series beside it and the honest statements (Gaussian model, the odds, the indifference point, the same-day discount)
            report only, never a pass: the fixed P10 gates still decide; nothing is written and no setting is changed
            --scope strategy needs --strategy ID (default scope: system)
        exit codes: 0 done (whatever the state), 1 refused (no series, a bad scope), 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static bool Handles(IReadOnlyList<string> args) => args.Count >= 1 && args[0] == "sequential";

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        if (!TryParse(args.Skip(1).ToList(), out var scope, out var strategy))
        {
            await console.Error.WriteAsync(Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope0 = services.CreateAsyncScope();
            var result = await scope0.ServiceProvider.GetRequiredService<IQueryHandler<GetSequentialStatusQuery, Result<SequentialStatusView>>>().HandleAsync(new GetSequentialStatusQuery(scope, strategy), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            foreach (var line in Lines(result.Value))
            {
                await console.Out.WriteLineAsync(SimulateOptions.Show(line));
            }

            return result.Value.HasSeries ? XdCommand.Done : XdCommand.Refused;
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

    internal static bool TryParse(List<string> args, out string scope, out string? strategy)
    {
        scope = GetSequentialStatusQuery.SystemScope;
        strategy = null;
        bool scopeSeen = false, strategySeen = false;
        for (var i = 0; i < args.Count; i += 2)
        {
            if (i + 1 >= args.Count)
            {
                return false;
            }

            switch (args[i])
            {
                case "--scope" when !scopeSeen && args[i + 1] is GetSequentialStatusQuery.SystemScope or GetSequentialStatusQuery.StrategyScope:
                    scope = args[i + 1];
                    scopeSeen = true;
                    break;
                case "--strategy" when !strategySeen && !string.IsNullOrWhiteSpace(args[i + 1]):
                    strategy = args[i + 1];
                    strategySeen = true;
                    break;
                default:
                    return false;
            }
        }

        return (scope == GetSequentialStatusQuery.StrategyScope) == (strategy is not null);
    }

    internal static IEnumerable<string> Lines(SequentialStatusView v)
    {
        yield return v.Line;
        yield return "scope: " + v.Scope + (v.Strategy is null ? string.Empty : " " + v.Strategy) + string.Create(CultureInfo.InvariantCulture, $"; paper epoch {(v.Epoch is { } e ? e.ToString(CultureInfo.InvariantCulture) : "none")}; settings {v.SettingsHash} ({v.Settings})");
        yield return string.Create(CultureInfo.InvariantCulture, $"boundaries: caution {v.Boundaries.Caution:0.000000} (odds {v.Boundaries.CautionOdds:0.##} to 1), failure {v.Boundaries.Failure:0.000000} (odds {v.Boundaries.FailureOdds:0.##} to 1), reflected at {v.Boundaries.OnTrack:0.000000} (a label, not a decision)");
        foreach (var (name, s) in new[] { ("edge series", v.Edge), ("P10 behavioural series", v.Behavioural) })
        {
            yield return name + ": " + s.StateText;
            yield return string.Create(CultureInfo.InvariantCulture, $"  sum {s.Sum:0.000000}, {s.TradesCounted} trade(s), effective n {s.EffectiveN:0.0}, lowest {s.Path.Lowest:0.000000} at trade {s.Path.LowestAtTrade}, sigma {s.Path.SigmaUsed:0.###}");
            if (s.FirstCautionTrade is { } c)
            {
                yield return string.Create(CultureInfo.InvariantCulture, $"  first at or below the caution boundary: trade {c} ({s.FirstCautionDate:yyyy-MM-dd})");
            }

            if (s.FirstFailureTrade is { } f)
            {
                yield return string.Create(CultureInfo.InvariantCulture, $"  first at or below the failure boundary: trade {f} ({s.FirstFailureDate:yyyy-MM-dd}); the flag stays for this epoch");
            }

            yield return "  basis: " + s.Basis;
            if (s.Note is not null)
            {
                yield return "  " + s.Note;
            }
        }

        foreach (var statement in v.Statements)
        {
            yield return statement;
        }
    }
}
