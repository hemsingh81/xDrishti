using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>An ISIN the owner typed for a name, to be remembered as a learned name.</summary>
internal sealed record LearnedToSave(string NameKey, string Isin, string Name);

/// <summary>What applying the owner's choices decided to remember: names pointed at a holding or an instrument (aliases, ADR 0028) and names given an ISIN (learned names, ADR 0029).</summary>
internal sealed record AppliedChoices(IReadOnlyList<AliasToSave> Aliases, IReadOnlyList<LearnedToSave> Learned)
{
    public static AppliedChoices None { get; } = new([], []);
}

/// <summary>
/// Applies the owner's choices of what a scrip name of a global transaction report means to the security index (ADR 0029). A choice names a scrip by the name as written or by its key (both reduced to the key names are compared by) and says exactly one of a holding of the
/// account, an instrument of the master list, or an ISIN. Choices for the same key that agree are one choice (remembered if any asks for it); choices that disagree are refused. Failure (<c>portfolio.invalid</c>, field <c>choices</c>) for a choice that names no scrip of the file,
/// no holding or instrument, an ISIN that is not one, or contradicts another. Pure.
/// </summary>
internal static class BillChoices
{
    public const int MaxChoices = ReportImportLimits.MaxChoices;

    public static Result<AppliedChoices> Apply(SecurityIndex index, IReadOnlyList<BillChoice> choices, IReadOnlySet<string> keysInFile)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(keysInFile);
        if (choices.Count == 0)
        {
            return Result.Success(AppliedChoices.None);
        }

        if (choices.Count > MaxChoices)
        {
            return Fail($"At most {MaxChoices:N0} choices.");
        }

        var chosen = new Dictionary<string, (BillChoice Choice, SecurityRef Reference)>(StringComparer.Ordinal);
        var aliases = new List<AliasToSave>();
        var learned = new List<LearnedToSave>();
        var remembered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var choice in choices)
        {
            if (choice is null)
            {
                return Fail("Empty choice.");
            }

            var name = BillName.Parse(choice.Name);
            if (!keysInFile.Contains(name.Key))
            {
                return Fail($"No row of the file has the name {Quote(choice.Name)}.");
            }

            var (problem, reference) = Pick(index, choice);
            if (problem is not null)
            {
                return Fail(problem);
            }

            if (chosen.TryGetValue(name.Key, out var earlier))
            {
                if (earlier.Choice.HoldingId != choice.HoldingId || earlier.Choice.InstrumentId != choice.InstrumentId || !string.Equals(earlier.Choice.Isin, choice.Isin, StringComparison.OrdinalIgnoreCase))
                {
                    return Fail($"The choices for the name {Quote(name.Key)} disagree: the file spells it more than one way and the choices point it at different places. Choose one place for it.");
                }
            }
            else
            {
                chosen[name.Key] = (choice, reference!);
                index.Choose(name.Key, reference!);
            }

            if (choice.Remember && remembered.Add(name.Key))
            {
                if (choice.Isin is { } isin)
                {
                    learned.Add(new LearnedToSave(name.Key, TradeRules.NormaliseIsin(isin)!, name.Display.Length > 0 ? name.Display : name.Key));
                }
                else
                {
                    aliases.Add(new AliasToSave(name.Key, choice.HoldingId, choice.InstrumentId));
                }
            }
        }

        return Result.Success(new AppliedChoices(aliases, learned));
    }

    private static (string? Problem, SecurityRef? Reference) Pick(SecurityIndex index, BillChoice choice)
    {
        var given = (choice.HoldingId is not null ? 1 : 0) + (choice.InstrumentId is not null ? 1 : 0) + (string.IsNullOrWhiteSpace(choice.Isin) ? 0 : 1);
        if (given != 1)
        {
            return ($"Choose exactly one of holdingId, instrumentId and isin for {Quote(choice.Name)}.", null);
        }

        if (choice.HoldingId is { } holding)
        {
            return index.Holding(holding) is { } found ? (null, found) : ($"The holding chosen for {Quote(choice.Name)} is not one of the account's.", null);
        }

        if (choice.InstrumentId is { } instrument)
        {
            return index.Share(instrument) is { } share ? (null, share) : ($"The instrument chosen for {Quote(choice.Name)} is not an NSE share or ETF of the instrument list.", null);
        }

        var isin = TradeRules.NormaliseIsin(choice.Isin)!;
        return Trades.TradeCells.IsIsin(isin) ? (null, index.ByIsin(isin)) : ($"{Quote(isin)} is not an ISIN (12 characters, for example INE000A01011).", null);
    }

    private static Result<AppliedChoices> Fail(string message) => Result.Failure<AppliedChoices>(PortfolioErrors.Invalid("choices", message));

    private static string Quote(string? name) => "\"" + new string([.. (name ?? string.Empty).Where(c => !char.IsControl(c)).Take(40)]) + "\"";
}
