namespace XDrishti.Application.Abstractions.OpeningBars;

/// <summary>The owner's stored switch <c>baskets.opening_bars</c> (BK-03b): insert-only rows, the newest counts, no row means OFF.</summary>
public interface IOpeningBarsSwitchStore
{
    ValueTask<bool> IsOnAsync(CancellationToken cancellationToken);

    /// <summary>Appends a row; <paramref name="actor"/> must be <c>owner:...</c> (the database checks it).</summary>
    ValueTask AppendAsync(bool enabled, string actor, string reason, CancellationToken cancellationToken);
}
