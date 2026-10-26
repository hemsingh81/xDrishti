using System.Globalization;
using System.Text;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Context;

namespace XDrishti.Api.Context;

/// <summary>
/// The <c>context</c> verb (P3-01d): <c>refresh</c> and <c>coverage</c> of the market context, a host step that calls the handlers on the precedent of <c>cost-sheet</c>; no decision lives here. Exit codes as the other verbs: 0 done, 1 refused, 2 the command line is
/// wrong, 3 an unexpected failure, 130 cancelled. A missing prerequisite (an index that is not tracked) is not a failure: it is printed with its fix and the exit code is 0.
/// </summary>
internal static class ContextCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(console);

        var (options, errors) = ContextOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(error);
            }

            await console.Error.WriteAsync(ContextOptions.Usage);
            return SimulateCommand.UsageError;
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ContextCommand).FullName!);
        try
        {
            await using var scope = services.CreateAsyncScope();
            return options.Action switch
            {
                ContextAction.Refresh => await RefreshAsync(scope.ServiceProvider, console, logger, cancellationToken),
                ContextAction.RebuildBreadth => await RebuildBreadthAsync(options, scope.ServiceProvider, console, cancellationToken),
                _ => await CoverageAsync(scope.ServiceProvider, console, logger, cancellationToken),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await console.Error.WriteLineAsync("cancelled");
            return SimulateCommand.Cancelled;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            await console.Error.WriteLineAsync("failed: " + SimulateCommand.Describe(failure));
            return SimulateCommand.Failed;
        }
    }

    private static async Task<int> RefreshAsync(IServiceProvider provider, SimulateConsole console, ILogger logger, CancellationToken cancellationToken)
    {
        var handler = provider.GetRequiredService<ICommandHandler<RefreshContextCommand, ContextRefreshed>>();
        var result = await handler.HandleAsync(new RefreshContextCommand(), cancellationToken);
        if (result.IsFailure)
        {
            await console.Error.WriteLineAsync(result.Error.Code + ": " + result.Error.Message);
            return SimulateCommand.Refused;
        }

        var done = result.Value;
        ContextLog.Refreshed(logger, done.Written, done.Pending, done.Skipped);
        var output = new StringBuilder();
        Line(output, "written", done.Written);
        Line(output, "waiting for the VIX", done.Pending);
        Line(output, "skipped", done.Skipped);
        output.Append("last day: ").Append(done.LastDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "none").Append('\n');
        if (done.Missing is not null)
        {
            output.Append("missing: ").Append(done.Missing).Append(" - ").Append(done.Fix).Append('\n');
        }

        await console.Out.WriteAsync(output.ToString());
        return SimulateCommand.Done;
    }

    private static async Task<int> RebuildBreadthAsync(ContextOptions options, IServiceProvider provider, SimulateConsole console, CancellationToken cancellationToken)
    {
        var handler = provider.GetRequiredService<ICommandHandler<RebuildBreadthCommand, BreadthRebuilt>>();
        var result = await handler.HandleAsync(new RebuildBreadthCommand(options.From!.Value, options.To!.Value, options.Write), cancellationToken);
        if (result.IsFailure)
        {
            await console.Error.WriteLineAsync(result.Error.Code + ": " + result.Error.Message);
            return SimulateCommand.Refused;
        }

        await console.Out.WriteAsync(Text(result.Value));
        return SimulateCommand.Done;
    }

    internal static string Text(BreadthRebuilt done)
    {
        var output = new StringBuilder();
        output.Append(done.Written ? "write: yes\n" : "write: no (dry run; add --write to store)\n");
        if (done.Missing is not null)
        {
            output.Append("missing: ").Append(done.Missing).Append(" - track shares on the Instruments screen and run Update data\n");
        }

        Line(output, "days looked at", done.Days);
        Line(output, "days " + (done.Written ? "changed" : "that would change"), done.Changes.Count);
        foreach (var change in done.Changes)
        {
            output.Append("  ").Append(change.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Append(" old ").Append(Figure(change.OldValue, change.OldState, change.OldVoters))
                .Append(" new ").Append(Figure(change.NewValue, change.NewState, change.NewVoters)).Append('\n');
        }

        output.Append("breadth of your tracked shares, not of the market\n");
        return output.ToString();
    }

    private static string Figure(double? value, string? state, int? voters) =>
        state is null ? "none" : (value is { } v ? v.ToString("0.0", CultureInfo.InvariantCulture) : state) + " (" + (voters?.ToString(CultureInfo.InvariantCulture) ?? "0") + " voters)";

    private static async Task<int> CoverageAsync(IServiceProvider provider, SimulateConsole console, ILogger logger, CancellationToken cancellationToken)
    {
        var handler = provider.GetRequiredService<IQueryHandler<GetContextCoverageQuery, ContextCoverageView>>();
        var view = await handler.HandleAsync(new GetContextCoverageQuery(), cancellationToken);
        ContextLog.Shown(logger, view.Days, view.SkippedTotal);
        await console.Out.WriteAsync(Text(view));
        return SimulateCommand.Done;
    }

    internal static string Text(ContextCoverageView v)
    {
        var output = new StringBuilder();
        output.Append("index: ").Append(v.IndexSymbol ?? "(not found)").Append('\n');
        output.Append("vix: ").Append(v.VixSymbol ?? "(not found)").Append('\n');
        if (v.Missing is not null)
        {
            output.Append("missing: ").Append(v.Missing).Append(" - ").Append(v.Fix).Append('\n');
        }

        Line(output, "days", v.Days);
        Date(output, "first day", v.FirstDate);
        Date(output, "last day", v.LastDate);
        output.Append("sessions behind: ").Append(v.AgeSessions?.ToString(CultureInfo.InvariantCulture) ?? "n/a").Append(v.Stale ? " (stale)" : string.Empty).Append('\n');
        Date(output, "index_trend readable from", v.IndexTrendReadableFrom);
        Date(output, "regime readable from", v.RegimeReadableFrom);
        Date(output, "vix_pct readable from", v.VixPctReadableFrom);
        Date(output, "regime with both parts from", v.RegimeWithVixReadableFrom);
        Line(output, "vix days", v.VixDays);
        Line(output, "days without a vix", v.VixMissingDays);
        output.Append("vix window span (calendar days): ").Append(v.VixWindowSpanDays?.ToString(CultureInfo.InvariantCulture) ?? "n/a").Append(v.VixWindowTooWide ? " (wider than 400: a gap)" : string.Empty).Append('\n');
        Line(output, "sessions without a record inside the range", v.GapSessions);
        Line(output, "skipped", v.SkippedTotal);
        foreach (var skipped in v.Skipped)
        {
            output.Append("  ").Append(skipped.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(' ').Append(skipped.Reason).Append(' ').Append(skipped.Bars.ToString(CultureInfo.InvariantCulture)).Append(" bars\n");
        }

        var b = v.Breadth;
        output.Append("breadth: ").Append(b.State is null ? "none yet" : (b.Latest is { } latest ? latest.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "insufficient") + " on " + b.LatestDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            + " (" + b.Voters?.ToString(CultureInfo.InvariantCulture) + " voted of " + b.Universe?.ToString(CultureInfo.InvariantCulture) + ", " + b.Excluded?.ToString(CultureInfo.InvariantCulture) + " left out)").Append('\n');
        Line(output, "days with breadth", b.ComputedDays);
        Line(output, "days waiting for breadth", b.PendingDays);
        Line(output, "days with too few voters", b.InsufficientDays);
        foreach (var date in b.InsufficientDates)
        {
            output.Append("  ").Append(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(" insufficient\n");
        }

        output.Append(b.Label).Append('\n');
        output.Append(v.Source).Append('\n');
        return output.ToString();
    }

    private static void Line(StringBuilder output, string label, int value) => output.Append(label).Append(": ").Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');

    private static void Date(StringBuilder output, string label, DateOnly? value) =>
        output.Append(label).Append(": ").Append(value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "n/a").Append('\n');
}
