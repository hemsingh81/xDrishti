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
            return options.Action == ContextAction.Refresh
                ? await RefreshAsync(scope.ServiceProvider, console, logger, cancellationToken)
                : await CoverageAsync(scope.ServiceProvider, console, logger, cancellationToken);
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

        output.Append(v.Source).Append('\n');
        return output.ToString();
    }

    private static void Line(StringBuilder output, string label, int value) => output.Append(label).Append(": ").Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');

    private static void Date(StringBuilder output, string label, DateOnly? value) =>
        output.Append(label).Append(": ").Append(value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "n/a").Append('\n');
}
