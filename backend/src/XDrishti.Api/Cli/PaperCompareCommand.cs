using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Paper;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary><c>xd paper compare</c> (spec P10-04, ADR 0122): prints the backtest-versus-paper report as text. Read only; exit 0 whatever the verdicts say.</summary>
internal static class PaperCompareCommand
{
    public const string Usage = """
        usage:
          xd paper compare [--epoch ID] [--draws N]
            the counted paper trades of the epoch against a composition-matched bootstrap of the backtest trades known before it started: mean and band, largest fall, the two exit statistics, the early warning
            draws 1000 to 100000 (default 10000); the same inputs give the same numbers; nothing is stored, no order exists
        exit codes: 0 done (whatever the verdicts), 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static bool Handles(IReadOnlyList<string> args) => args.Count >= 1 && args[0] == "compare";

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        if (!TryParse(args.Skip(1).ToList(), out var epoch, out var draws))
        {
            await console.Error.WriteAsync(Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetBacktestVsPaperQuery, Result<BacktestVsPaperView>>>()
                .HandleAsync(new GetBacktestVsPaperQuery(epoch, draws), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            foreach (var line in Lines(result.Value))
            {
                await console.Out.WriteLineAsync(SimulateOptions.Show(line));
            }

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

    internal static IEnumerable<string> Lines(BacktestVsPaperView v)
    {
        var c = v.Counts;
        yield return string.Create(CultureInfo.InvariantCulture, $"epoch {(v.EpochId?.ToString(CultureInfo.InvariantCulture) ?? "-")}: {v.N} counted trades ({c.Reserve} reserve, {c.Straddle} straddle, {c.Open} open, {c.OtherEpochs} in other epochs), basis {v.Basis}");
        yield return "paper mean R: " + Show(v.Paper.Mean) + "; 90% interval " + Show(v.Paper.Ci90.Lo) + " to " + Show(v.Paper.Ci90.Hi);
        yield return "backtest band (5th to 95th percentile): " + Show(v.Band.P05) + " to " + Show(v.Band.P95) + "; paper is " + (v.Band.Position ?? "not placed") + "; rank " + Show(v.Band.Rank);
        yield return "largest fall (paper): " + Show(v.Paper.MaxDrawdownR) + "; backtest 90th percentile at the same n: " + Show(v.Drawdown.P90) + "; " + (v.Drawdown.Position ?? "not placed");
        yield return "E3 expectancy: " + v.Criteria.Expectancy.Verdict + " - " + v.Criteria.Expectancy.Label;
        yield return "E4 drawdown: " + v.Criteria.Drawdown.Verdict + " - " + v.Criteria.Drawdown.Label;
        if (v.EarlyWarning is not null)
        {
            yield return "EARLY WARNING: " + v.EarlyWarning;
        }

        foreach (var cell in v.Cells)
        {
            yield return $"cell {cell.CellKey}: paper n {cell.PaperN}, reference n {cell.ReferenceN}, {cell.State}{(cell.Reason is null ? string.Empty : " (" + cell.Reason + ")")}";
        }

        yield return $"{v.SensitivityAllProposals.Label}: n {v.SensitivityAllProposals.N}, mean {Show(v.SensitivityAllProposals.Mean)} (shown beside, not used above)";
        yield return "basis: " + v.Evidence.Statement;
    }

    private static string Show(MetricView m) =>
        m.Number is { } n
            ? n.ToString("0.000", CultureInfo.InvariantCulture) + (m.State == "provisional" ? " (provisional)" : string.Empty)
            : m.State + (m.Reason is null ? string.Empty : " (" + m.Reason + ")");

    private static bool TryParse(List<string> args, out long? epoch, out int? draws)
    {
        epoch = null;
        draws = null;
        for (var i = 0; i < args.Count; i += 2)
        {
            if (i + 1 >= args.Count)
            {
                return false;
            }

            switch (args[i])
            {
                case "--epoch" when long.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var e) && epoch is null:
                    epoch = e;
                    break;
                case "--draws" when int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var d) && draws is null:
                    draws = d;
                    break;
                default:
                    return false;
            }
        }

        return true;
    }
}
