using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.LiveTrading;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Api.Cli;

/// <summary><c>xd live compliance show</c> (spec P11-05): prints the compliance records as text, the newest of each kind first and then the history. Read only: a record is written in the app (the Compliance tab of /live), never from here.</summary>
internal static class LiveComplianceCommand
{
    public const string Usage = """
        usage:
          xd live compliance show [--kind static_ip|sebi_recheck|dhan_ip_read_policy] [--limit 1..200]
            prints the newest record of each kind ("empty" where nothing is recorded: that is not "no change") and the history, newest first
            the record is your note of what you were told; nothing is checked at Dhan or the ISP, and nothing is written from here
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var parsed = Parse(args);
        if (parsed.Errors.Count > 0)
        {
            foreach (var error in parsed.Errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetComplianceQuery, Result<ComplianceView>>>().HandleAsync(new GetComplianceQuery(parsed.Kind, parsed.Limit), cancellationToken);
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

    internal static string Render(ComplianceView view)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("latest per kind");
        foreach (var (kind, record) in new[] { (ComplianceKinds.StaticIp, view.Latest.StaticIp), (ComplianceKinds.SebiRecheck, view.Latest.SebiRecheck), (ComplianceKinds.DhanIpReadPolicy, view.Latest.DhanIpReadPolicy) })
        {
            text.AppendLine(record is null ? $"  {kind}: empty" : "  " + Line(record));
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"history ({view.Items.Count.ToString(CultureInfo.InvariantCulture)})");
        foreach (var record in view.Items)
        {
            text.AppendLine("  " + Line(record));
        }

        return text.ToString();
    }

    private static string Line(ComplianceRecordView r) =>
        SimulateOptions.Show($"{r.Kind} #{r.Id.ToString(CultureInfo.InvariantCulture)} {r.Outcome} decided {r.DecidedOn:yyyy-MM-dd} next review {r.NextReviewOn:yyyy-MM-dd}{(r.Overdue ? " OVERDUE" : string.Empty)}{(r.StaticIpState is null ? string.Empty : " state " + r.StaticIpState)}{(r.LockEndsOn is null ? string.Empty : $" lock ends {r.LockEndsOn:yyyy-MM-dd}")} - {r.Summary}");

    private static (string? Kind, int Limit, List<string> Errors) Parse(IReadOnlyList<string> args)
    {
        var errors = new List<string>();
        string? kind = null;
        var limit = 50;
        if (args.Count == 0 || args[0] != "show")
        {
            errors.Add("a command is required: show");
            return (null, limit, errors);
        }

        for (var i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--kind" when i + 1 < args.Count:
                    kind = args[++i];
                    if (!ComplianceKinds.All.Contains(kind))
                    {
                        errors.Add("--kind is static_ip, sebi_recheck or dhan_ip_read_policy");
                    }

                    break;
                case "--limit" when i + 1 < args.Count:
                    if (!int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 200)
                    {
                        errors.Add("--limit is 1 to 200");
                        limit = 50;
                    }

                    break;
                default:
                    errors.Add($"unknown or incomplete option '{SimulateOptions.Show(args[i])}'");
                    break;
            }
        }

        return (kind, limit, errors);
    }
}
