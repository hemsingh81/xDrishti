using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.OptionCosts;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd options spread-model build [--underlying SYMBOL]</c> (spec P14-07): builds the half-spread model of the option cost model from the usable chain sessions the quality checks found, by calling the use case; no decision lives here. Prints counts and words only.
/// Exit codes: 0 built or nothing to build (the reason is printed), 2 wrong command line or an unknown underlying, 3 failure, 130 cancelled.
/// </summary>
internal static class OptionsSpreadModelCommand
{
    public const string Usage = """
        usage:
          xd options spread-model build [--underlying SYMBOL]
            builds the half-spread model of each underlying (or the one named, as NSE:NIFTY) that has enough usable chain sessions; fewer sessions is not an error, the reason is printed; the same sessions and settings build nothing twice
        exit codes: 0 built or nothing to build, 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(console);
        var (command, errors) = Parse(args);
        if (command is null)
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
            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<BuildSpreadModelCommand, SpreadModelBuildSummary>>().HandleAsync(command, cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.UsageError;
            }

            if (result.Value.Reason is { } reason)
            {
                await console.Out.WriteLineAsync("nothing to build: " + reason);
            }

            foreach (var line in result.Value.Lines)
            {
                await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"{SimulateOptions.Show(line.Symbol)}: {line.Status}: {line.Reason}{(line.ModelId is null ? string.Empty : " (model " + line.ModelId[..12] + ")")}"));
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

    internal static (BuildSpreadModelCommand? Command, List<string> Errors) Parse(IReadOnlyList<string> args)
    {
        var errors = new List<string>();
        if (args.Count == 0 || args[0] != "build")
        {
            errors.Add("the verb is 'xd options spread-model build'");
            return (null, errors);
        }

        string? underlying = null;
        for (var i = 1; i < args.Count; i++)
        {
            if (args[i] == "--underlying" && i + 1 < args.Count && underlying is null)
            {
                underlying = args[++i];
                if (underlying.Length is 0 or > 40 || underlying.Any(char.IsControl))
                {
                    errors.Add("--underlying is a canonical symbol such as NSE:NIFTY");
                }
            }
            else
            {
                errors.Add($"unknown, repeated or incomplete argument '{SimulateOptions.Show(args[i])}'");
            }
        }

        return errors.Count > 0 ? (null, errors) : (new BuildSpreadModelCommand(underlying), errors);
    }
}
