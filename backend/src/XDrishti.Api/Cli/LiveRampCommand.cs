using System.Globalization;
using System.Text;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.LiveTrading;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary><c>xd live ramp show</c> (spec P11-04b): prints the ramp of the live accounts as text - the step, the scale, a hold, the evidence gates with their states, the breach rules firing, today's loss and the halts. Read only: every owner act is made in the app (the Ramp tab of /live) with the password typed again; nothing is written, read from Dhan or placed from here.</summary>
internal static class LiveRampCommand
{
    public const string Usage = """
        usage:
          xd live ramp show [--account GUID]
            prints, per manual-live account (or the one given): the step and scale, a hold, G1 to G7 with state, number, threshold and n, the breach rules firing now,
            the loss of today (a loss that cannot be measured says so; it is never "no loss") and the halts of the last 30 days
            a halt is advice: you place every order yourself; nothing is written from here
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (account, errors) = Parse(args);
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
            var accounts = await sp.GetRequiredService<IAccountStore>().ListActiveAsync(cancellationToken);
            var live = accounts.Where(a => a.Trading is { Enabled: true, Mode: TradingMode.ManualLive } && (account is null || a.Id == account)).ToList();
            if (live.Count == 0)
            {
                await console.Out.WriteLineAsync("no manual-live account");
                return XdCommand.Done;
            }

            var handler = sp.GetRequiredService<IQueryHandler<GetRampQuery, Result<RampView>>>();
            foreach (var a in live)
            {
                var result = await handler.HandleAsync(new GetRampQuery(a.Id), cancellationToken);
                if (result.IsFailure)
                {
                    await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                    return XdCommand.Refused;
                }

                await console.Out.WriteAsync(Render(a.Name, result.Value));
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

    internal static string Render(string name, RampView r)
    {
        var text = new StringBuilder();
        text.AppendLine(SimulateOptions.Show(string.Create(CultureInfo.InvariantCulture, $"{name}: {(r.Step is { } s ? "S" + s.ToString(CultureInfo.InvariantCulture) : "no step")}, scale {Num(r.ScaleEffective)}, live risk {Num(r.LiveRiskPct)} %{(r.Hold ? ", ON HOLD" : string.Empty)}, {(r.Eligible ? "eligible to move up" : "not eligible to move up")}, {r.Sessions.ToString(CultureInfo.InvariantCulture)} sessions at this step")));
        foreach (var g in r.Gates)
        {
            text.AppendLine(SimulateOptions.Show(string.Create(CultureInfo.InvariantCulture, $"  {g.Code} {g.State} number {Num(g.Number)} needed {Num(g.Threshold)} n {(g.N is { } n ? n.ToString(CultureInfo.InvariantCulture) : "not known")}: {g.Reason}")));
        }

        foreach (var b in r.Breaches)
        {
            text.AppendLine(SimulateOptions.Show($"  breach {b.Code} ({b.Action}): {b.Reason}"));
        }

        if (r.Loss is { } l)
        {
            text.AppendLine(SimulateOptions.Show(string.Create(CultureInfo.InvariantCulture, $"  loss {l.Date:yyyy-MM-dd}: {l.State}{(l.LossR is { } lr ? ", " + lr.ToString("0.00", CultureInfo.InvariantCulture) + " R of " + Num(l.LimitR) + " R" : ", no figure")} (capture {l.Health})")));
        }

        foreach (var h in r.Halts)
        {
            text.AppendLine(SimulateOptions.Show(string.Create(CultureInfo.InvariantCulture, $"  halt {h.Kind} from {h.PeriodStart:yyyy-MM-dd}: {Num(h.MeasuredR)} R of {Num(h.LimitR)} R ({h.MeasureState}), {h.Action}")));
        }

        text.AppendLine("  no setting lets this system place an order");
        return text.ToString();
    }

    private static string Num(double? v) => v is { } x ? x.ToString("0.###", CultureInfo.InvariantCulture) : "not known";

    private static (Guid? Account, List<string> Errors) Parse(IReadOnlyList<string> args)
    {
        var errors = new List<string>();
        Guid? account = null;
        if (args.Count == 0 || args[0] != "show")
        {
            errors.Add("a command is required: show");
            return (null, errors);
        }

        for (var i = 1; i < args.Count; i++)
        {
            if (args[i] == "--account" && i + 1 < args.Count)
            {
                if (Guid.TryParse(args[++i], out var id))
                {
                    account = id;
                }
                else
                {
                    errors.Add("--account is an account id (a GUID)");
                }
            }
            else
            {
                errors.Add($"unknown or incomplete option '{SimulateOptions.Show(args[i])}'");
            }
        }

        return (account, errors);
    }
}
