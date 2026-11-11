namespace XDrishti.Application.Abstractions.OpeningBars;

/// <summary>One change of the owner's opening-bars switch (<c>market.opening_bars_switch</c>, insert-only).</summary>
public sealed record OpeningBarsSwitchRow(long Id, bool Enabled, string Actor, string Reason, DateTimeOffset ChangedAt);

/// <summary>The owner's stored switch <c>baskets.opening_bars</c> (BK-03b): insert-only rows, the newest counts, no row means OFF.</summary>
public interface IOpeningBarsSwitchStore
{
    ValueTask<bool> IsOnAsync(CancellationToken cancellationToken);

    /// <summary>The changes, newest first.</summary>
    ValueTask<IReadOnlyList<OpeningBarsSwitchRow>> HistoryAsync(int take, CancellationToken cancellationToken);

    /// <summary>Appends a row (the database stamps the time and checks that the actor is <c>owner:...</c>) and returns it.</summary>
    ValueTask<OpeningBarsSwitchRow> AppendAsync(bool enabled, string actor, string reason, CancellationToken cancellationToken);
}
