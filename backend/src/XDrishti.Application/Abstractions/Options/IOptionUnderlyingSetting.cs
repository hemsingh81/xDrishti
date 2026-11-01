namespace XDrishti.Application.Abstractions.Options;

/// <summary>The setting <c>options.underlyings</c> (ADR 0002, ADR 0175): the owner's list of option underlyings, kept as an app setting row.</summary>
public interface IOptionUnderlyingSetting
{
    /// <summary>The stored value, or null when there is no row yet.</summary>
    ValueTask<string?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The canonical symbols of the F&amp;O-eligible, active tracked shares with the highest mean turnover (close x volume) over their last 20 daily bars before
    /// <paramref name="before"/>, at most <paramref name="take"/>, ties by symbol, and the India date of the newest daily bar those figures rest on (null without one).
    /// </summary>
    ValueTask<RankedShares> MostTradedFnoSharesAsync(DateOnly before, int take, CancellationToken cancellationToken);

    /// <summary>Writes the row only when there is none (the default is fixed once and never recomputed); false when a row was there already.</summary>
    ValueTask<bool> FixDefaultAsync(string value, string actor, DateTimeOffset now, CancellationToken cancellationToken);
}
