using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Paper;
using XDrishti.Domain.Paper;

namespace XDrishti.Api.Cli;

/// <summary><c>xd paper review</c> and <c>xd paper discipline</c> (spec P10-03, ADR 0121): call the use cases and print. No decision lives here; nothing is applied and nothing is an order.</summary>
internal static class PaperReviewCommand
{
    private const string Actor = "cli:xd";

    public const string Usage = """
        usage:
          xd paper review [--week YYYY-Www] [--store]
            builds the weekly paper review from stored records and PRINTS it; nothing is stored unless --store is given (default week: this week, India time)
            --store keeps it, once per ISO week and epoch, only for the current week from its Saturday 10:00 India time or the most recent completed week; an older week can only be printed
            a week that has a review shows the stored one with --store and writes nothing
          xd paper discipline [--from YYYY-MM-DD] [--to YYYY-MM-DD]
            prints the discipline report of the epoch (or the days given): the seven categories, what skipping and resizing cost in R with n and state, added fills, the execution gap
          both only read: nothing is applied, approved or ordered
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Whether the arguments after <c>paper</c> are one of these verbs.</summary>
    public static bool Handles(IReadOnlyList<string> args) => args.Count >= 1 && args[0] is "review" or "discipline";

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        var rest = args.Skip(1).ToList();
        var review = args[0] == "review";
        var parsed = Parse(rest, review);
        if (parsed is null)
        {
            await console.Error.WriteAsync(Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var p = scope.ServiceProvider;
            if (review)
            {
                var result = await p.GetRequiredService<ICommandHandler<BuildPaperReviewCommand, PaperReviewView>>()
                    .HandleAsync(new BuildPaperReviewCommand(parsed.GetValueOrDefault("week"), null, parsed.ContainsKey("store"), Actor), cancellationToken);
                if (result.IsFailure)
                {
                    await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                    return XdCommand.Refused;
                }

                var view = result.Value;
                await console.Out.WriteAsync(view.Text);
                await console.Out.WriteLineAsync(view.Id is null ? "(printed only: nothing stored; use --store to keep it)" : view.Repeated ? $"(review {view.Id} was stored already; nothing written)" : $"(stored as review {view.Id})");
                return XdCommand.Done;
            }

            var query = new GetDisciplineQuery(null, Date(parsed, "from"), Date(parsed, "to"));
            var discipline = await p.GetRequiredService<IQueryHandler<GetDisciplineQuery, XDrishti.Domain.Common.Result<DisciplineView>>>().HandleAsync(query, cancellationToken);
            if (discipline.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(discipline.Error.Code + ": " + discipline.Error.Message));
                return XdCommand.Refused;
            }

            await PrintAsync(console, discipline.Value);
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

    private static async Task PrintAsync(SimulateConsole console, DisciplineView view)
    {
        await console.Out.WriteLineAsync(ReviewText.PaperOnly);
        await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"Discipline {view.From:yyyy-MM-dd} to {view.To:yyyy-MM-dd}: {view.Evidence.PrimaryTickets} primary tickets, {view.Evidence.Sessions} closed sessions."));
        if (view.Unavailable is not null)
        {
            await console.Out.WriteLineAsync("unavailable: " + SimulateOptions.Show(view.Unavailable));
            return;
        }

        foreach (var line in view.Lines)
        {
            await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"- {line.Title}: n {line.N}; sum {Show(line.SumR)}; mean {Show(line.RMetric)}; {SimulateOptions.Show(line.Note)}"));
        }

        foreach (var row in view.Execution)
        {
            await console.Out.WriteLineAsync($"- {row.Label}: {Show(row.Value)}");
        }

        foreach (var limit in view.Limits)
        {
            await console.Out.WriteLineAsync("Limit: " + limit);
        }
    }

    private static string Show(XDrishti.Application.Reporting.MetricView metric) =>
        metric.Number is { } n ? n.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "R" + (metric.State == "provisional" ? $" (provisional: {SimulateOptions.Show(metric.Reason ?? string.Empty)})" : string.Empty) : $"{metric.State}: {SimulateOptions.Show(metric.Reason ?? string.Empty)}";

    private static DateOnly? Date(Dictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var text) ? DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

    private static Dictionary<string, string>? Parse(List<string> args, bool review)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            var key = args[i];
            if (review && key == "--store")
            {
                result["store"] = "1";
            }
            else if (key == (review ? "--week" : "--from") || (!review && key == "--to"))
            {
                if (i + 1 >= args.Count || result.ContainsKey(key[2..]) || !Valid(key, args[i + 1]))
                {
                    return null;
                }

                result[key[2..]] = args[++i];
            }
            else
            {
                return null;
            }
        }

        return result;
    }

    private static bool Valid(string key, string value) =>
        key == "--week" ? PaperReviewWeek.TryParse(value) is not null : DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
