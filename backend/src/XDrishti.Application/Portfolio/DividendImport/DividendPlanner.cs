using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>
/// Decides what importing a dividend file would do (ADR 0028). <see cref="Plan"/> gives each distinct row a status — <c>imported</c> (the account stores its identity already), <c>possible_duplicate</c> (equal to a dividend the owner typed in: same
/// holding, date and amount), <c>ambiguous</c>, <c>needs_match</c> or <c>new</c> — and its warnings: the paid amount differing from per share × quantity by more than ₹0.01 (the paid amount wins), a date before the account's first lot, a match by part
/// of the name. <c>Digest</c> fingerprints every row's outcome, so an import can tell that what the owner saw has changed. <see cref="Select"/> applies the owner's choices and ticked duplicates and says what to store. Pure: no store, no clock.
/// </summary>
internal static class DividendPlanner
{
    /// <summary>The paid amount may differ from per share × quantity by this many rupees before a row is flagged.</summary>
    public const decimal MismatchTolerance = 0.01m;

    public static DividendPlan Plan(
        DividendFile file,
        IReadOnlyList<IdentifiedDividend> identified,
        DividendNameIndex index,
        IReadOnlySet<string> storedIds,
        IReadOnlyList<StoredDividendKey> manual,
        DateOnly? firstLotDate)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(identified);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(storedIds);
        ArgumentNullException.ThrowIfNull(manual);
        var typedIn = new DividendManualIndex(manual);
        var rows = new List<PlannedDividend>(identified.Count);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(digest, string.Create(CultureInfo.InvariantCulture, $"v1|{file.BadTotal}|{file.Repeated}|{firstLotDate:yyyy-MM-dd}"));
        foreach (var (row, externalId) in identified.Select(i => (i.Row, i.ExternalId)))
        {
            var match = index.Match(row);
            var status = StatusOf(externalId, row, match, storedIds, typedIn);
            rows.Add(new PlannedDividend(row, externalId, status, match, Warnings(row, match, firstLotDate)));
            Append(digest, Fingerprint(row, status, match));
        }

        return new DividendPlan(file, rows, firstLotDate, Convert.ToHexStringLower(digest.GetHashAndReset()), index, storedIds, typedIn);
    }

    /// <summary>
    /// What to store when the owner imports: the rows that are to be added (new, needs a match, ambiguous) and the possible duplicates the owner ticked (<paramref name="includeDuplicates"/>, by line), each with the match that counts — the owner's
    /// choice for its name, else the plan's — and the aliases to remember. A choice names a scrip by the name as the file wrote it or by the item's <c>nameKey</c>; both are reduced to the key names are compared by, so two spellings of one company are one
    /// scrip: choices for the same key that agree are one choice (remembered if any asks for it), choices that disagree are refused. A row whose choice makes it equal to a typed-in dividend is a possible duplicate then and is stored only if ticked. Failure
    /// (<c>portfolio.invalid</c>, field <c>choices</c>) for a choice that names no row of the file, no holding of the account or no share of the master, that is not exactly one of holding, instrument and none, or that contradicts another for the same scrip.
    /// </summary>
    public static Result<DividendSelection> Select(DividendPlan plan, IReadOnlyList<DividendChoice> choices, IReadOnlySet<int> includeDuplicates)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(includeDuplicates);
        var chosen = Choices(plan, choices);
        if (chosen.IsFailure)
        {
            return Result.Failure<DividendSelection>(chosen.Error);
        }

        var store = new List<SelectedDividend>();
        var skipped = 0;
        var already = 0;
        foreach (var planned in plan.Rows)
        {
            if (planned.Status == DividendStatus.Imported)
            {
                already++;
                continue;
            }

            var match = chosen.Value.Matches.GetValueOrDefault(planned.Row.NameKey) ?? planned.Match;
            var status = chosen.Value.Matches.ContainsKey(planned.Row.NameKey) ? StatusOf(planned.ExternalId, planned.Row, match, plan.StoredIds, plan.Manual) : planned.Status;
            if (status == DividendStatus.PossibleDuplicate && !includeDuplicates.Contains(planned.Row.Line))
            {
                skipped++;
                continue;
            }

            store.Add(new SelectedDividend(planned, match));
        }

        return Result.Success(new DividendSelection(store, skipped, already, chosen.Value.Aliases));
    }

    private static Result<ChosenMatches> Choices(DividendPlan plan, IReadOnlyList<DividendChoice> choices)
    {
        if (choices.Count > DividendCsvLimits.MaxChoices)
        {
            return Fail($"At most {DividendCsvLimits.MaxChoices:N0} choices.");
        }

        var keysInFile = plan.Rows.Select(r => r.Row.NameKey).ToHashSet(StringComparer.Ordinal);
        var matches = new Dictionary<string, (DividendChoice Choice, DividendMatch Match)>(StringComparer.Ordinal);
        var remember = new HashSet<string>(StringComparer.Ordinal);
        var aliases = new List<AliasToSave>();
        foreach (var choice in choices)
        {
            if (choice is null)
            {
                return Fail("Empty choice.");
            }

            var key = DividendNames.Key(choice.Name);
            if (!keysInFile.Contains(key))
            {
                return Fail($"No row of the file has the name {Quote(choice.Name)}.");
            }

            var (problem, match) = Pick(plan.Index, choice);
            if (problem is not null)
            {
                return Fail(problem);
            }

            if (matches.TryGetValue(key, out var earlier))
            {
                if (!Agree(earlier.Choice, choice))
                {
                    return Fail($"The choices for the name {Quote(key)} disagree: the file spells it more than one way and the choices point it at different places. Choose one place for it.");
                }
            }
            else
            {
                matches[key] = (choice, match!);
            }

            if (choice.Remember && remember.Add(key))
            {
                aliases.Add(new AliasToSave(key, choice.HoldingId, choice.InstrumentId));
            }
        }

        return Result.Success(new ChosenMatches(matches.ToDictionary(m => m.Key, m => m.Value.Match, StringComparer.Ordinal), aliases));
    }

    /// <summary>Two choices for one scrip agree when they point at the same holding, the same instrument or both at nothing.</summary>
    private static bool Agree(DividendChoice first, DividendChoice second) => first.HoldingId == second.HoldingId && first.InstrumentId == second.InstrumentId && first.None == second.None;

    private static Result<ChosenMatches> Fail(string message) => Result.Failure<ChosenMatches>(PortfolioErrors.Invalid("choices", message));

    /// <summary>The match a choice stands for, or the problem when it is not exactly one of holding, instrument and none, or points at something that does not exist.</summary>
    private static (string? Problem, DividendMatch? Match) Pick(DividendNameIndex index, DividendChoice choice)
    {
        var given = (choice.HoldingId is not null ? 1 : 0) + (choice.InstrumentId is not null ? 1 : 0) + (choice.None ? 1 : 0);
        if (given != 1)
        {
            return ($"Choose exactly one of holdingId, instrumentId and none for {Quote(choice.Name)}.", null);
        }

        if (choice.HoldingId is { } holding)
        {
            return index.Holding(holding) is { } found
                ? (null, new DividendMatch(DividendMatchKind.Choice, found, null, []))
                : ($"The holding chosen for {Quote(choice.Name)} is not one of the account's.", null);
        }

        if (choice.InstrumentId is { } instrument)
        {
            return index.Share(instrument) is { } share
                ? (null, new DividendMatch(DividendMatchKind.Choice, null, share, []))
                : ($"The instrument chosen for {Quote(choice.Name)} is not an NSE share or ETF of the instrument list.", null);
        }

        return (null, new DividendMatch(DividendMatchKind.Choice, null, null, []));
    }

    private static DividendStatus StatusOf(string externalId, ParsedDividend row, DividendMatch match, IReadOnlySet<string> storedIds, DividendManualIndex manual)
    {
        if (storedIds.Contains(externalId))
        {
            return DividendStatus.Imported;
        }

        if (manual.Has(row, match))
        {
            return DividendStatus.PossibleDuplicate;
        }

        if (match.IsAmbiguous)
        {
            return DividendStatus.Ambiguous;
        }

        return match.Kind == DividendMatchKind.None ? DividendStatus.NeedsMatch : DividendStatus.New;
    }

    private static List<string> Warnings(ParsedDividend row, DividendMatch match, DateOnly? firstLot)
    {
        var warnings = new List<string>(3);
        if (row.PerShare is { } share && row.Quantity is { } qty && Math.Abs((share * qty) - row.Amount) > MismatchTolerance)
        {
            warnings.Add(DividendStatuses.AmountMismatch);
        }

        if (firstLot is { } first && row.PayDate < first)
        {
            warnings.Add(DividendStatuses.BeforeFirstLot);
        }

        if (match.Kind == DividendMatchKind.Contains && match.IsMatched)
        {
            warnings.Add(DividendStatuses.PartialNameMatch);
        }

        return warnings;
    }

    private static string Fingerprint(ParsedDividend row, DividendStatus status, DividendMatch match) => string.Create(
        CultureInfo.InvariantCulture,
        $"{row.Line}|{row.PayDate:yyyy-MM-dd}|{row.Amount:0.00}|{(int)status}|{(int)match.Kind}|{match.HoldingId:N}|{match.InstrumentId:N}|{string.Join(',', match.Candidates.Select(c => c.Id.ToString("N")))}");

    private static void Append(IncrementalHash hash, string text)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(text));
        hash.AppendData("\n"u8);
    }

    private static string Quote(string? name) => "\"" + new string([.. (name ?? string.Empty).Where(c => !char.IsControl(c)).Take(40)]) + "\"";

    private sealed record ChosenMatches(Dictionary<string, DividendMatch> Matches, List<AliasToSave> Aliases);
}

/// <summary>A row to store with the match that counts for it (the owner's choice, else the plan's).</summary>
internal sealed record SelectedDividend(PlannedDividend Planned, DividendMatch Match);

/// <summary>An alias to remember: the name key and where it points (a holding, an instrument, or neither: unallocated).</summary>
internal sealed record AliasToSave(string NameKey, Guid? HoldingId, Guid? InstrumentId);

/// <summary>What an import stores: the rows, how many possible duplicates were left out, how many rows were stored already, and the aliases to remember.</summary>
internal sealed record DividendSelection(IReadOnlyList<SelectedDividend> ToStore, int PossibleDuplicatesSkipped, int AlreadyImported, IReadOnlyList<AliasToSave> Aliases);
