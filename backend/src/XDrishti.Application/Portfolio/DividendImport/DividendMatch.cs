using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>Which rule decided what a dividend's scrip name means (ADR 0028), in the order the rules are tried; <c>Choice</c> is the owner's pick in the import request.</summary>
internal enum DividendMatchKind
{
    Alias,
    Isin,
    Symbol,
    Name,
    Contains,
    Master,
    Choice,
    None,
}

/// <summary>
/// What a dividend's scrip name means: one holding of the account (<see cref="Holding"/>), one share of the instrument master that is not held (<see cref="Share"/>), nothing (an unallocated dividend), or — when more than one holding fits and nothing
/// is guessed — <see cref="Candidates"/> to choose from (<see cref="IsAmbiguous"/>).
/// </summary>
internal sealed record DividendMatch(DividendMatchKind Kind, HoldingData? Holding, ShareName? Share, IReadOnlyList<HoldingData> Candidates)
{
    public static DividendMatch None { get; } = new(DividendMatchKind.None, null, null, []);

    public bool IsAmbiguous => Candidates.Count > 1;

    public Guid? HoldingId => Holding?.Id;

    public Guid? InstrumentId => Holding is not null ? Holding.InstrumentId : Share?.Id;

    /// <summary>True when the dividend gets a holding or an instrument (nothing is left to match).</summary>
    public bool IsMatched => Holding is not null || Share is not null;

    public static DividendMatch Of(DividendMatchKind kind, HoldingData holding) => new(kind, holding, null, []);

    public static DividendMatch Of(DividendMatchKind kind, ShareName share) => new(kind, null, share, []);

    public static DividendMatch Between(DividendMatchKind kind, IReadOnlyList<HoldingData> candidates) => new(kind, null, null, candidates);
}
