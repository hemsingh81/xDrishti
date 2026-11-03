using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Market.Eod;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Eod;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Cli;

/// <summary>
/// <c>xd data eod-probe|eod-load|eod-actions|eod-universe|eod-check</c> (CF-04 Part A): the owner's verbs for the NSE daily files. A host step that parses arguments and calls the use cases; no decision lives here. <c>eod-probe</c> and <c>eod-load</c> make requests only
/// while <c>Data:History:Nse:Enabled</c> is on and the endpoint is on the allow-list (else they are refused and nothing is sent). <c>eod-actions</c> reads a local file into the existing import PREVIEW; it applies nothing. Exit codes: 0 done, 1 refused,
/// 2 wrong command line, 3 failure, 130 cancelled.
/// </summary>
internal static class DataEodCommand
{
    public const string Usage = """
        usage:
          xd data eod-probe --dates YYYY-MM-DD[,YYYY-MM-DD...]   fetch those days' raw files once into data/history-probe and print their header lines (needs Data:History:Nse:Enabled and the allow-list entries of ADR 0195)
          xd data eod-load [--from YYYY-MM-DD] [--to YYYY-MM-DD]   load the daily files of the days not yet loaded (same switch); resumable
          xd data eod-actions FILE.csv                             read NSE's corporate-actions file into the import PREVIEW (applies nothing)
          xd data eod-universe --as-of YYYY-MM-DD                  the securities of the daily table on that date
          xd data eod-check --from YYYY-MM-DD --to YYYY-MM-DD      daily-versus-minute findings (a finding, never a gate)
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(console);
        var (parsed, errors) = Parse(args);
        if (parsed is null)
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
            switch (parsed.Verb)
            {
                case "eod-probe":
                    return await Report(await sp.GetRequiredService<ICommandHandler<EodProbeCommand, EodProbeReport>>().HandleAsync(new EodProbeCommand(parsed.Dates), cancellationToken), r => r.Lines, console);
                case "eod-load":
                    return await Report(await sp.GetRequiredService<ICommandHandler<LoadEodCommand, EodLoadSummary>>().HandleAsync(new LoadEodCommand(parsed.From, parsed.To), cancellationToken), r => [r.Text], console);
                case "eod-actions":
                    string text;
                    try
                    {
                        text = await File.ReadAllTextAsync(parsed.File!, cancellationToken);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        await console.Error.WriteLineAsync("the file cannot be read");
                        return XdCommand.UsageError;
                    }

                    return await Report(await sp.GetRequiredService<ICommandHandler<PreviewNseActionsCommand, NseActionsPreview>>().HandleAsync(new PreviewNseActionsCommand(text), cancellationToken), Describe, console);
                case "eod-universe":
                    var rows = await sp.GetRequiredService<IQueryHandler<ShowEodUniverseQuery, IReadOnlyList<EodUniverseRow>>>().HandleAsync(new ShowEodUniverseQuery(parsed.From!.Value), cancellationToken);
                    await console.Out.WriteLineAsync($"{rows.Count} securities on {parsed.From:yyyy-MM-dd} (isin, symbol, series, known since, in catalog)");
                    foreach (var row in rows)
                    {
                        await console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"{row.Isin ?? "-"} {row.Symbol} {row.Series} {row.KnownSince:yyyy-MM-dd} {(row.InCatalog ? "in catalog" : "not in catalog")}"));
                    }

                    return XdCommand.Done;
                default:
                    return await Report(
                        await sp.GetRequiredService<IQueryHandler<CheckDailyAgainstMinuteQuery, Result<IReadOnlyList<DailyMinuteFinding>>>>().HandleAsync(new CheckDailyAgainstMinuteQuery(parsed.From!.Value, parsed.To!.Value), cancellationToken),
                        f => f.Count == 0 ? ["no findings"] : [.. f.Select(x => $"{x.Code} {x.Symbol} {x.TradeDate:yyyy-MM-dd} {x.Field}: {x.Message}")],
                        console);
            }
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

    internal static (Parsed? Command, List<string> Errors) Parse(IReadOnlyList<string> args)
    {
        var errors = new List<string>();
        var verb = args.Count > 0 ? args[0] : string.Empty;
        if (verb is not ("eod-probe" or "eod-load" or "eod-actions" or "eod-universe" or "eod-check"))
        {
            return (null, ["name a verb: eod-probe, eod-load, eod-actions, eod-universe or eod-check"]);
        }

        List<DateOnly> dates = [];
        DateOnly? from = null, to = null;
        string? file = null;
        for (var i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--dates" when verb == "eod-probe" && i + 1 < args.Count:
                    dates.AddRange(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(d => Date(d, "--dates", errors)).OfType<DateOnly>());
                    break;
                case "--from" when verb is "eod-load" or "eod-check" && i + 1 < args.Count:
                    from = Date(args[++i], "--from", errors);
                    break;
                case "--to" when verb is "eod-load" or "eod-check" && i + 1 < args.Count:
                    to = Date(args[++i], "--to", errors);
                    break;
                case "--as-of" when verb == "eod-universe" && i + 1 < args.Count:
                    from = Date(args[++i], "--as-of", errors);
                    break;
                default:
                    if (verb == "eod-actions" && file is null && !args[i].StartsWith("--", StringComparison.Ordinal))
                    {
                        file = args[i];
                    }
                    else
                    {
                        errors.Add($"unknown or incomplete argument '{SimulateOptions.Show(args[i])}'");
                    }

                    break;
            }
        }

        if (errors.Count == 0)
        {
            if (verb == "eod-probe" && dates.Count == 0)
            {
                errors.Add("--dates is required");
            }
            else if (verb == "eod-actions" && file is null)
            {
                errors.Add("name the corporate-actions file");
            }
            else if (verb == "eod-universe" && from is null)
            {
                errors.Add("--as-of is required");
            }
            else if (verb == "eod-check" && (from is null || to is null))
            {
                errors.Add("--from and --to are required");
            }
        }

        return errors.Count > 0 ? (null, errors) : (new Parsed(verb, dates, from, to, file), errors);
    }

    private static IReadOnlyList<string> Describe(NseActionsPreview p) =>
    [
        $"file: {p.Splits} splits, {p.Bonuses} bonuses, {p.Other} other lines left out, {p.Unreadable} unreadable lines left out",
        $"import preview (nothing applied): {p.Preview.Importable} importable, {p.Preview.Duplicates} duplicates skipped, {p.Preview.Invalid} invalid, {p.Preview.UnknownSymbols.Count} unknown symbols, {p.Preview.AdjustsCsvTwice ?? 0} would adjust the loaded CSV twice",
    ];

    private static async Task<int> Report<T>(Result<T> result, Func<T, IReadOnlyList<string>> lines, SimulateConsole console)
    {
        if (result.IsFailure)
        {
            await console.Error.WriteLineAsync(SimulateOptions.Show(result.Error.Code + ": " + result.Error.Message));
            return XdCommand.Refused;
        }

        foreach (var line in lines(result.Value))
        {
            await console.Out.WriteLineAsync(line);
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

    internal sealed record Parsed(string Verb, IReadOnlyList<DateOnly> Dates, DateOnly? From, DateOnly? To, string? File);
}
