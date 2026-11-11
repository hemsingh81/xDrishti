using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd learn cells</c> (spec P6-01, ADR 0064): computes the cell statistics of stored runs as of a date and keeps the snapshot, by calling the use case the API reads from. No decision lives here. Exit codes follow the other <c>xd</c> commands.
/// </summary>
internal static class LearnCellsCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        var (options, errors) = LearnCellsOptions.Parse(args);
        if (options is null)
        {
            foreach (var error in errors)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(error));
            }

            await console.Error.WriteAsync(LearnCellsOptions.Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<ComputeCellStatsCommand, CellStatsOutcome>>()
                .HandleAsync(new ComputeCellStatsCommand(options.AsOf, options.Sources, options.Options, "cli:xd", options.ByRegime, options.ByUniverse), cancellationToken);
            if (result.IsFailure)
            {
                await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
                return XdCommand.Refused;
            }

            var o = result.Value;
            await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"cell statistics as of {options.AsOf:yyyy-MM-dd}: {o.Cells} cells, {o.Rows} rows from {o.Trades} trades, snapshot {o.SnapshotKey[..12]} ({(o.AlreadyStored ? "already stored" : "stored")})"));
            if (o.Regime is { } regime)
            {
                var reasons = regime.UnavailableByReason.Count == 0 ? "none" : string.Join(", ", regime.UnavailableByReason.Select(r => $"{r.Key} {r.Value}"));
                await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"regime split: {o.RegimeCells} regime cells; {regime.Labelled} of {regime.Total} trades have a regime ({regime.IndexAndVix} from the index and the VIX, {regime.IndexOnly} from the index alone); no regime: {reasons}"));
            }

            if (o.Universe is { } universe)
            {
                await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"universe split: {universe.TierCells} tier cells; {universe.Tiered} trades in a tier, {universe.Unknown} with no tier (too few bars or no dated set), {universe.Unavailable} dated when too few shares could be ranked"));
            }

            if (o.TauFloorShare is { } share)
            {
                await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"shrinkage: {o.ShrunkRows} rows shrunk, {o.TauFloorRows} ({share:P0}) with the between-cell variance at its floor"));
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
}
