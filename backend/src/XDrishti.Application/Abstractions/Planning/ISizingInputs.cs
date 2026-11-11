using XDrishti.Domain.Selection.SizeScaling;

namespace XDrishti.Application.Abstractions.Planning;

/// <summary>A basket budget that applies to an instrument: the basket's name (as the plan context names it) and its share of the total open risk limit, in percent.</summary>
public sealed record BasketBudgetRef(string BasketName, double BudgetPct);

/// <summary>What the scaling reads about one instrument as of the previous close. A null part is unavailable, never a pass.</summary>
/// <param name="Volatility">The volatility reading; null when its bars could not be read.</param>
/// <param name="Adv20">The 20-session median traded value in rupees; null when it cannot be computed.</param>
/// <param name="Budgets">The budgets of the baskets that hold it; empty when none has a budget row.</param>
public sealed record SymbolSizingInputs(VolReading? Volatility, double? Adv20, IReadOnlyList<BasketBudgetRef> Budgets);

/// <summary>The owner's settings (with the version row), and the inputs by symbol (ordinal, upper case).</summary>
public sealed record SizingInputs(SizingSettings Settings, long Version, IReadOnlyDictionary<string, SymbolSizingInputs> Symbols);

/// <summary>Where the plan builder reads the inputs of the down-only size scaling (spec BK-10). Read only: nothing behind it writes or places anything.</summary>
public interface ISizingInputs
{
    /// <summary>The settings and the per-symbol inputs for the instruments, as of the close of <paramref name="session"/>; nothing after it may be read.</summary>
    ValueTask<SizingInputs> ReadAsync(DateOnly session, DateOnly forDate, IReadOnlyList<ContextInstrument> instruments, CancellationToken cancellationToken);
}
