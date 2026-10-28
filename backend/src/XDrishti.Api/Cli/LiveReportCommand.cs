using System.Globalization;
using System.Text;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.LiveTrading;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd live report</c> (spec P11-03): prints the summary and the verdict of the live-versus-paper report of one live account. Reads stored rows only; nothing is read from or sent to Dhan and nothing is placed. Exit codes as the other <c>xd live</c> commands.
/// </summary>
internal static class LiveReportCommand
{
    public const string Usage = """
        usage:
          xd live report --account ID [--from YYYY-MM-DD] [--to YYYY-MM-DD]
            prints the pairs, the mean shortfall in R with its interval, the verdict and the exclusions
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (account, from, to, errors) = Parse(args);
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
            var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<LiveVsPaperQuery, Result<LiveVsPaperView>>>();
            var result = await handler.HandleAsync(new LiveVsPaperQuery(account, null, from, to, null, null, 1, LiveReportRules.MaxPageSize), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            await console.Out.WriteAsync(Render(result.Value));
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

    internal static string Render(LiveVsPaperView view)
    {
        var s = view.Summary;
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"live vs paper ({view.State}, basis {view.Evidence.Basis}, {s.NPairs} pairs)");
        text.AppendLine(CultureInfo.InvariantCulture, $"  mean shortfall R (positive is live worse): {Show(s.Mean)}, 95 % interval {Show(s.Lo95)} to {Show(s.Hi95)}, one-sided upper {Show(s.UpperOneSided95)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"  entry part {Show(s.EntryDiffMean)}, exit part {Show(s.ExitDiffMean)}, charges part {Show(s.ChargesDiffMean)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"  verdict {s.Verdict} (margin {s.Threshold:0.00} R, needs {s.MinPairs} pairs)");
        foreach (var e in view.Evidence.Exclusions)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  left out {e.Reason}: {e.Count}");
        }

        return text.ToString();
    }

    private static string Show(MetricView m) => m.Number is { } n ? n.ToString("0.0000", CultureInfo.InvariantCulture) : m.State;

    private static (Guid Account, DateOnly? From, DateOnly? To, List<string> Errors) Parse(IReadOnlyList<string> args)
    {
        var errors = new List<string>();
        Guid account = default;
        DateOnly? from = null, to = null;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--account" when i + 1 < args.Count:
                    if (!Guid.TryParse(args[++i], out account))
                    {
                        errors.Add("--account is a GUID");
                    }

                    break;
                case "--from" or "--to" when i + 1 < args.Count:
                    var which = args[i];
                    if (DateOnly.TryParseExact(args[++i], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    {
                        if (which == "--from")
                        {
                            from = d;
                        }
                        else
                        {
                            to = d;
                        }
                    }
                    else
                    {
                        errors.Add(which + " is YYYY-MM-DD");
                    }

                    break;
                default:
                    errors.Add($"unknown or incomplete option '{SimulateOptions.Show(args[i])}'");
                    break;
            }
        }

        if (account == default && errors.Count == 0)
        {
            errors.Add("--account is required");
        }

        return (account, from, to, errors);
    }
}
