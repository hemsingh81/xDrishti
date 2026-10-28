using System.Globalization;
using System.Text;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.LiveTrading;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd live capture</c> and <c>xd live outcomes</c> (spec P11-01): <c>capture</c> reads the day's trade book (and the order book when its switch is on) once, then settles the date; <c>outcomes</c> prints the stored outcomes of a day. Everything is read only at Dhan:
/// the only requests are the two GETs of the allow-list. Nothing is placed, changed or cancelled.
/// </summary>
internal static class LiveCaptureCommand
{
    public const string Usage = """
        usage:
          xd live capture [--account ID] [--date YYYY-MM-DD]
            one read-only poll of today's trade book (and order book when Live:Capture:OrderBook:Enabled is on), then the settle of the date (default today) and the measuring of its executions against the plan (P11-02)
          xd live outcomes [--account ID] [--date YYYY-MM-DD]
            prints the stored outcome of every approved live ticket of the date (default today); nothing is read from Dhan
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (command, account, date, errors) = Parse(args);
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
            if (command == "capture")
            {
                var poll = await scope.ServiceProvider.GetRequiredService<ICommandHandler<CaptureLiveSessionCommand, LiveCaptureOutcome>>().HandleAsync(new CaptureLiveSessionCommand(account, true), cancellationToken);
                if (poll.IsFailure)
                {
                    await console.Error.WriteLineAsync(SimulateOptions.Show(poll.Error.Code + ": " + poll.Error.Message));
                    return XdCommand.Refused;
                }

                await console.Out.WriteLineAsync("poll: " + SimulateOptions.Show(poll.Value.Summary));
                var settle = await scope.ServiceProvider.GetRequiredService<ICommandHandler<SettleLiveSessionCommand, LiveSettleOutcome>>().HandleAsync(new SettleLiveSessionCommand(date, account), cancellationToken);
                if (settle.IsFailure)
                {
                    await console.Error.WriteLineAsync(SimulateOptions.Show(settle.Error.Code + ": " + settle.Error.Message));
                    return XdCommand.Refused;
                }

                await console.Out.WriteLineAsync("settle: " + SimulateOptions.Show(settle.Value.Summary));
                var measured = await scope.ServiceProvider.GetRequiredService<ICommandHandler<ComputeLiveExecutionsCommand, LiveExecutionsOutcome>>().HandleAsync(new ComputeLiveExecutionsCommand(date, account), cancellationToken);
                if (measured.IsFailure)
                {
                    await console.Error.WriteLineAsync(SimulateOptions.Show(measured.Error.Code + ": " + measured.Error.Message));
                    return XdCommand.Refused;
                }

                await console.Out.WriteLineAsync("executions: " + SimulateOptions.Show(measured.Value.Summary));
                return poll.Value.Failed > 0 || settle.Value.Failed > 0 || measured.Value.Failed > 0 ? XdCommand.Failed : XdCommand.Done;
            }

            var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(330));
            var page = await scope.ServiceProvider.GetRequiredService<IQueryHandler<LiveOutcomesQuery, Result<LiveOutcomesPage>>>().HandleAsync(new LiveOutcomesQuery(account, day, day, 1, 200), cancellationToken);
            if (page.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(page.Error.Code + ": " + page.Error.Message));
                return XdCommand.Refused;
            }

            await console.Out.WriteAsync(Render(page.Value));
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

    internal static string Render(LiveOutcomesPage page)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"outcomes ({page.Total.ToString(CultureInfo.InvariantCulture)})");
        foreach (var o in page.Items)
        {
            var missed = o.MissedR is { } r ? " missedR " + r.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty;
            var flags = o.Flags.Count > 0 ? " flags " + string.Join(',', o.Flags) : string.Empty;
            text.AppendLine(SimulateOptions.Show(string.Create(CultureInfo.InvariantCulture, $"  item {o.ItemId} {o.Date:yyyy-MM-dd} {o.Outcome} allocated {o.AllocatedQty} entry {o.EntryQty} evidence {o.OrderEvidence} protection {o.Protection}{missed}{flags}")));
        }

        return text.ToString();
    }

    private static (string Command, Guid? Account, DateOnly? Date, List<string> Errors) Parse(IReadOnlyList<string> args)
    {
        var errors = new List<string>();
        Guid? account = null;
        DateOnly? date = null;
        var command = args.Count > 0 ? args[0] : string.Empty;
        if (command is not ("capture" or "outcomes"))
        {
            errors.Add("a command is required: capture or outcomes");
            return (command, null, null, errors);
        }

        for (var i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--account" when i + 1 < args.Count:
                    if (Guid.TryParse(args[++i], out var id))
                    {
                        account = id;
                    }
                    else
                    {
                        errors.Add("--account is an account id");
                    }

                    break;
                case "--date" when i + 1 < args.Count:
                    if (DateOnly.TryParseExact(args[++i], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    {
                        date = d;
                    }
                    else
                    {
                        errors.Add("--date is YYYY-MM-DD");
                    }

                    break;
                default:
                    errors.Add($"unknown or incomplete option '{SimulateOptions.Show(args[i])}'");
                    break;
            }
        }

        return (command, account, date, errors);
    }
}
