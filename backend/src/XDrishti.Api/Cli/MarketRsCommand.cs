using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Strength;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd market rs --from D --to D2</c> (BK-09): computes the relative-strength rows of the sessions in the range that have none. It never overwrites: a session that already has rows is left as it is and listed. A host step that parses arguments and calls the use
/// case; it needs <c>Market:RelativeStrength:Enabled</c>. Exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled.
/// </summary>
internal static class MarketRsCommand
{
    public const string Usage = """
        usage:
          xd market rs --from YYYY-MM-DD --to YYYY-MM-DD   compute the relative-strength rows of the sessions in the range that have none (insert-once: a session that has rows is left as it is and listed; needs Market:RelativeStrength:Enabled)
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(console);
        var (from, to, errors) = Parse(args);
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
            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<ComputeRelativeStrengthCommand, RelativeStrengthComputed>>()
                .HandleAsync(new ComputeRelativeStrengthCommand(from, to), cancellationToken);
            return await Report(result, console);
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

    internal static (DateOnly? From, DateOnly? To, List<string> Errors) Parse(IReadOnlyList<string> args)
    {
        var errors = new List<string>();
        DateOnly? from = null, to = null;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--from" when i + 1 < args.Count:
                    from = Date(args[++i], "--from", errors);
                    break;
                case "--to" when i + 1 < args.Count:
                    to = Date(args[++i], "--to", errors);
                    break;
                default:
                    errors.Add($"unknown or incomplete argument '{SimulateOptions.Show(args[i])}'");
                    break;
            }
        }

        if (errors.Count == 0 && (from is null || to is null))
        {
            errors.Add("--from and --to are required");
        }
        else if (errors.Count == 0 && from > to)
        {
            errors.Add("--from is not after --to");
        }

        return (from, to, errors);
    }

    private static async Task<int> Report(Result<RelativeStrengthComputed> result, SimulateConsole console)
    {
        if (result.IsFailure)
        {
            await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
            return XdCommand.Refused;
        }

        var done = result.Value;
        if (done.Missing is not null)
        {
            await console.Error.WriteLineAsync("refused: " + done.Missing);
            return XdCommand.Refused;
        }

        await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"written: {done.Written.Count} sessions, {done.Rows} rows"));
        if (done.Existing.Count > 0)
        {
            await console.Out.WriteLineAsync("already stored, left as they are: " + string.Join(", ", done.Existing.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));
        }

        return XdCommand.Done;
    }

    private static DateOnly? Date(string text, string name, List<string> errors)
    {
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        errors.Add($"{name} is a date as YYYY-MM-DD");
        return null;
    }
}
