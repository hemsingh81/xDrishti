namespace XDrishti.Domain.Market.Context;

/// <summary>
/// The regime of a day, or the reason there is none (P6-02, ADR 0146). It exists only where the stored context returned a regime for the date: the regime of the latest stored day strictly before <paramref name="Date"/> (<paramref name="ContextAsOf"/>), with its
/// <paramref name="Basis"/> (<see cref="ContextBasis"/>). Otherwise <paramref name="Unavailable"/> names why (<see cref="ContextRead"/>'s reasons or <see cref="RegimeNotReady"/>); no default, no previous label, no <c>*</c>.
/// </summary>
/// <param name="Date">The date the label is for (the decision day).</param>
/// <param name="Code">1 to 9, null when unavailable.</param>
/// <param name="Name">The regime name, null when unavailable.</param>
/// <param name="Basis"><see cref="ContextBasis.IndexAndVix"/> or <see cref="ContextBasis.IndexOnly"/>, null when unavailable.</param>
/// <param name="ContextAsOf">The context day the regime was read from; null when unavailable and no day was read.</param>
/// <param name="Unavailable">The reason when there is no label, null otherwise.</param>
public sealed record RegimeLabel(DateOnly Date, int? Code, string? Name, string? Basis, DateOnly? ContextAsOf, string? Unavailable)
{
    /// <summary>The context day exists but its <c>regime</c> is not ready (fewer than 265 days before it).</summary>
    public const string RegimeNotReady = "regime_not_ready";

    public bool IsAvailable => Code is not null;

    /// <summary>The regime part of a cell key: the name, with <see cref="RegimeNames.IndexOnlySuffix"/> when built from the index alone. Only for an available label.</summary>
    public string CellPart => IsAvailable
        ? Basis == ContextBasis.IndexOnly ? Name + RegimeNames.IndexOnlySuffix : Name!
        : throw new InvalidOperationException("An unavailable regime label has no cell part.");

    /// <summary>The label of <paramref name="date"/> from what the context says as of that date.</summary>
    public static RegimeLabel Of(DateOnly date, ContextRead read)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (read.Figures is not { } figures)
        {
            return new RegimeLabel(date, null, null, null, null, read.Unavailable ?? ContextRead.NoContext);
        }

        if (figures.Regime is { } value && double.IsFinite(value) && value is >= 1 and <= 9 && value == Math.Floor(value))
        {
            var code = (int)value;
            return new RegimeLabel(date, code, RegimeNames.NameOf(code), figures.Basis, figures.AsOf, null);
        }

        return new RegimeLabel(date, null, null, null, figures.AsOf, RegimeNotReady);
    }
}
