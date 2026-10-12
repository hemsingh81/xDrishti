using System.Globalization;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Import;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Actions;

/// <summary>
/// Imports corporate actions from CSV text (header <c>symbol,exDate,kind,numerator,denominator</c>, any case and order; an optional <c>note</c> column; <c>RELIANCE</c> or <c>NSE:RELIANCE</c>), applied at once with source csv.
/// With <paramref name="Preview"/> nothing is written: the counts say what an import would do. Symbols that match no listed instrument are listed and never guessed; lines that repeat an existing action (any status)
/// or an earlier line are skipped; lines with a problem are reported (first 50) and skipped, the rest is imported. <c>data.invalid</c> for empty text, a missing column, a text over 1,000,000 characters or over 5,000 data lines. One audit
/// event with counts and symbols, never notes.
/// <para>
/// A line of the other kind on the same ex-date as an applied action of the instrument, or as an earlier line of the text, is a problem line (a suggestion of the other kind is declined by itself, as when adding).
/// The loaded CSV files are adjusted at source: importable lines with an ex-date on or before the date their instrument's files reach are counted (<c>adjustsCsvTwice</c>) and listed (<c>warnings</c>); importing
/// them (not a preview) needs <paramref name="AcknowledgeCsvAdjusted"/>, else <c>data.csv_already_adjusted</c> (409) with the counts.
/// </para>
/// </summary>
public sealed record ImportCorporateActionsCommand(string? Text, bool Preview, string Actor, bool AcknowledgeCsvAdjusted = false) : ICommand;

internal sealed class ImportCorporateActionsHandler(
    IListedInstruments listed,
    ICorporateActionQueries queries,
    ICorporateActionStore store,
    IMarketUnitOfWork unitOfWork,
    IAccountEventLog events,
    TimeProvider clock) : ICommandHandler<ImportCorporateActionsCommand, ImportCorporateActionsResult>
{
    private static readonly string[] Required = ["symbol", "exdate", "kind", "numerator", "denominator"];

    private sealed record Row(int Line, string Canonical, ActionInput Input);

    private sealed record Parsed(int Lines, List<Row> Rows, int Invalid, List<ImportLineError> Errors);

    private sealed record Plan(List<(Guid InstrumentId, string Symbol, ActionInput Input, int Line)> New, int Duplicates, List<ImportLineError> Conflicts, List<(CorporateAction Action, string Symbol)> Declined, List<ImportLineWarning> Warnings, int AdjustsTwice, int AdjustedInstruments, DateOnly? FirstThrough);

    public async ValueTask<Result<ImportCorporateActionsResult>> HandleAsync(ImportCorporateActionsCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var parsed = await ParseAsync(command.Text, IndiaTime.Today(now), cancellationToken);
        if (parsed.IsFailure)
        {
            return Result.Failure<ImportCorporateActionsResult>(parsed.Error);
        }

        var rows = parsed.Value.Rows;
        var found = rows.Count == 0
            ? new Dictionary<string, IReadOnlyList<Guid>>(StringComparer.Ordinal)
            : new Dictionary<string, IReadOnlyList<Guid>>(await listed.FindAsync([.. rows.Select(r => r.Canonical).Distinct(StringComparer.Ordinal)], cancellationToken), StringComparer.Ordinal);
        var ids = rows.Select(r => found.TryGetValue(r.Canonical, out var list) && list.Count == 1 ? list[0] : (Guid?)null).ToList();
        var unknown = rows.Where((_, i) => ids[i] is null).Select(r => r.Canonical).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var known = rows.Select((r, i) => (Row: r, Id: ids[i])).Where(x => x.Id is not null).Select(x => (x.Row, Id: x.Id!.Value)).ToList();

        for (var attempt = 0; attempt < CorporateActionChecks.Attempts; attempt++)
        {
            var plan = await PlanAsync(known, cancellationToken);
            if (command.Preview || plan.New.Count == 0)
            {
                return Result.Success(Summarize(command.Preview, parsed.Value, unknown, plan, 0));
            }

            if (plan.AdjustsTwice > 0 && !command.AcknowledgeCsvAdjusted)
            {
                return Result.Failure<ImportCorporateActionsResult>(DataErrors.CsvAlreadyAdjusted(plan.AdjustsTwice, plan.AdjustedInstruments, plan.AdjustedInstruments == 1 ? plan.FirstThrough : null));
            }

            foreach (var (instrumentId, _, input, _) in plan.New)
            {
                store.Add(CorporateAction.Apply(instrumentId, input.ExDate, input.Kind, input.Numerator, input.Denominator, CorporateActionSource.Csv, input.Note, command.Actor, now));
            }

            foreach (var (suggestion, symbol) in plan.Declined.Where(d => d.Action.Ignore(command.Actor, now)))
            {
                events.Record(AccountEvent.Record(AccountEventType.CorporateActionIgnored, null, SameDayActions.Describe(suggestion, symbol), command.Actor, now));
            }

            events.Record(AccountEvent.Record(AccountEventType.CorporateActionsImported, null, Describe(plan, parsed.Value.Invalid + plan.Conflicts.Count, unknown.Count), command.Actor, now));
            if (await unitOfWork.TrySaveAsync(cancellationToken))
            {
                return Result.Success(Summarize(false, parsed.Value, unknown, plan, plan.New.Count));
            }
        }

        return Result.Failure<ImportCorporateActionsResult>(DataErrors.Invalid("csv", "The corporate actions changed meanwhile. Try again."));
    }

    private static ImportCorporateActionsResult Summarize(bool preview, Parsed parsed, List<string> unknown, Plan plan, int imported) =>
        new(
            preview,
            parsed.Lines,
            plan.New.Count,
            imported,
            plan.Duplicates,
            parsed.Invalid + plan.Conflicts.Count,
            [.. unknown.Take(CorporateActionChecks.MaxListedSymbols)],
            [.. parsed.Errors.Concat(plan.Conflicts).OrderBy(e => e.Line).Take(CorporateActionChecks.MaxListedErrors)],
            plan.AdjustsTwice,
            [.. plan.Warnings.Take(CorporateActionChecks.MaxListedErrors)]);

    private async ValueTask<Plan> PlanAsync(List<(Row Row, Guid Id)> known, CancellationToken cancellationToken)
    {
        var ids = known.Select(k => k.Id).Distinct().ToList();
        var existing = known.Count == 0 ? new HashSet<ActionKey>() : [.. await queries.ListKeysAsync(ids, cancellationToken)];
        var active = known.Count == 0 ? [] : await store.ListNotIgnoredAsync(ids, cancellationToken);
        var through = known.Count == 0 ? new Dictionary<Guid, DateOnly>() : new Dictionary<Guid, DateOnly>(await queries.ListCsvAdjustedThroughAsync(ids, cancellationToken));
        var fresh = new List<(Guid InstrumentId, string Symbol, ActionInput Input, int Line)>();
        var conflicts = new List<ImportLineError>();
        var declined = new List<(CorporateAction, string)>();
        var warnings = new List<ImportLineWarning>();
        var duplicates = 0;
        foreach (var (row, id) in known)
        {
            if (existing.Contains(new ActionKey(id, row.Input.ExDate, row.Input.Kind)))
            {
                duplicates++;
                continue;
            }

            var others = SameDayActions.OthersOn(active, id, row.Input.ExDate, row.Input.Kind, null);
            if (SameDayActions.Blocks(others) || fresh.Any(f => f.InstrumentId == id && f.Input.ExDate == row.Input.ExDate && f.Input.Kind != row.Input.Kind))
            {
                conflicts.Add(new ImportLineError(row.Line, "kind: The instrument has a split or bonus of the other kind on this ex-date (applied, or on an earlier line). Ignore it first."));
                continue;
            }

            existing.Add(new ActionKey(id, row.Input.ExDate, row.Input.Kind));
            declined.AddRange(others.Where(o => o.Status == CorporateActionStatus.Suggested).Select(o => (o, row.Canonical)));
            fresh.Add((id, row.Canonical, row.Input, row.Line));
            if (CsvAdjustment.AdjustsTwice(row.Input.ExDate, through, id))
            {
                warnings.Add(new ImportLineWarning(
                    row.Line,
                    row.Canonical,
                    row.Input.ExDate,
                    through[id],
                    $"The loaded CSV files of this instrument are already adjusted up to {through[id]:yyyy-MM-dd}: an action on {row.Input.ExDate:yyyy-MM-dd} would adjust their prices twice."));
            }
        }

        var twice = warnings.Select(w => w.Symbol).Distinct(StringComparer.Ordinal).ToList();
        return new Plan(fresh, duplicates, conflicts, declined, warnings, warnings.Count, twice.Count, warnings.Count == 0 ? null : warnings[0].CsvAdjustedThrough);
    }

    private static string Describe(Plan plan, int invalid, int unknown)
    {
        var symbols = plan.New.Select(n => n.Symbol).Distinct(StringComparer.Ordinal).ToList();
        var shown = string.Join(", ", symbols.Take(5)) + (symbols.Count > 5 ? $" and {symbols.Count - 5} more" : string.Empty);
        return $"CSV import: {plan.New.Count} added, {plan.Duplicates} duplicates skipped, {unknown} unknown symbols, {invalid} invalid lines; {shown}";
    }

    private static async ValueTask<Result<Parsed>> ParseAsync(string? text, DateOnly today, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Result.Failure<Parsed>(DataErrors.Invalid("csv", "Send the CSV text with the header symbol,exDate,kind,numerator,denominator."));
        }

        if (text.Length > CorporateActionChecks.MaxImportCharacters)
        {
            return Result.Failure<Parsed>(DataErrors.Invalid("csv", $"Send at most {CorporateActionChecks.MaxImportCharacters} characters."));
        }

        try
        {
            using var stringReader = new StringReader(text);
            var reader = new CsvRecordReader(stringReader);
            var headerLine = await NextAsync(reader, cancellationToken);
            if (headerLine is null)
            {
                return Result.Failure<Parsed>(DataErrors.Invalid("csv", "Send the CSV text with the header symbol,exDate,kind,numerator,denominator."));
            }

            var columns = Columns(await reader.SplitAsync(headerLine, cancellationToken));
            if (Required.Any(name => !columns.ContainsKey(name)))
            {
                return Result.Failure<Parsed>(DataErrors.Invalid("header", "The first line needs the columns symbol, exDate, kind, numerator and denominator."));
            }

            var rows = new List<Row>();
            var errors = new List<ImportLineError>();
            var invalid = 0;
            var lines = 0;
            while (await NextAsync(reader, cancellationToken) is { } line)
            {
                var number = (int)reader.RecordNumber;
                lines++;
                if (lines > CorporateActionChecks.MaxImportLines)
                {
                    return Result.Failure<Parsed>(DataErrors.Invalid("csv", $"Import at most {CorporateActionChecks.MaxImportLines} lines at a time."));
                }

                var fields = await reader.SplitAsync(line, cancellationToken);
                var row = ReadRow(number, fields, columns, today, out var problem);
                if (row is null)
                {
                    invalid++;
                    errors.Add(new ImportLineError(number, problem!));
                }
                else
                {
                    rows.Add(row);
                }
            }

            return Result.Success(new Parsed(lines, rows, invalid, errors));
        }
        catch (FormatException ex)
        {
            return Result.Failure<Parsed>(DataErrors.Invalid("csv", ex.Message));
        }
    }

    // The next line that is not blank.
    private static async ValueTask<string?> NextAsync(CsvRecordReader reader, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        return null;
    }

    private static Dictionary<string, int> Columns(string[] header)
    {
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < header.Length; i++)
        {
            var name = header[i].Trim().Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            columns.TryAdd(name, i);
        }

        return columns;
    }

    private static Row? ReadRow(int number, string[] fields, Dictionary<string, int> columns, DateOnly today, out string? problem)
    {
        string? Cell(string name) => columns.TryGetValue(name, out var at) && at < fields.Length ? fields[at].Trim() : null;

        problem = null;
        var symbol = Cell("symbol");
        var ticker = symbol is not null && symbol.StartsWith("NSE:", StringComparison.OrdinalIgnoreCase) ? symbol[4..] : symbol;
        if (string.IsNullOrWhiteSpace(ticker) || !ticker.All(c => char.IsAsciiLetterOrDigit(c) || c is '&' or '-'))
        {
            problem = "symbol: Use the symbol, for example RELIANCE or NSE:RELIANCE.";
            return null;
        }

        int? numerator = int.TryParse(Cell("numerator"), NumberStyles.None, CultureInfo.InvariantCulture, out var top) ? top : null;
        int? denominator = int.TryParse(Cell("denominator"), NumberStyles.None, CultureInfo.InvariantCulture, out var bottom) ? bottom : null;
        var input = CorporateActionChecks.Check(Cell("exdate"), Cell("kind"), numerator, denominator, Cell("note"), null, today);
        if (input.IsFailure)
        {
            var first = input.Error.Fields is { Count: > 0 } errs ? errs[0] : null;
            problem = first is null ? input.Error.Message : $"{first.Field}: {first.Message}";
            return null;
        }

        return new Row(number, CanonicalSymbol.ForListed(ticker.ToUpperInvariant()), input.Value);
    }
}
