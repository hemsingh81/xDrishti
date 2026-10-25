using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Strategies.Model;

/// <summary>
/// A strategy as a typed value, schema 1 (ADR 0036): the identity, the style, the entry timeframes it may run on, the sides, at most <see cref="MaxParams"/> numeric parameters, the rules of each side, the time rules and
/// the filter names. <see cref="Compilation.StrategyCompiler"/> checks it and compiles one side for one evaluation timeframe. Records compare by value and the lists compare element by element, so two definitions built from the same
/// data are equal (the round trip of the YAML reader and the seeding of the built-ins compare definitions).
/// </summary>
public sealed record StrategyDefinition(
    int Schema,
    string Id,
    Style Style,
    IReadOnlyList<Timeframe> Timeframes,
    IReadOnlyList<Side> Sides,
    IReadOnlyList<StrategyParam> Params,
    SideRules? Buy,
    SideRules? Sell,
    TimeRules? TimeRules,
    IReadOnlyList<string> Filters)
{
    /// <summary>The schema this library reads and writes.</summary>
    public const int CurrentSchema = 1;

    /// <summary>The most parameters a strategy declares.</summary>
    public const int MaxParams = 4;

    /// <summary>The most items in one condition list.</summary>
    public const int MaxItemsPerList = 100;

    /// <summary>The version of the mirror table that produced <see cref="Sell"/> from the BUY side (spec P3-02e), or null when SELL is hand-written or absent. Part of the canonical definition, so a new table is a new hash.</summary>
    public string? SellMirroredWith { get; init; }

    /// <inheritdoc />
    public bool Equals(StrategyDefinition? other) =>
        other is not null
        && Schema == other.Schema && Id == other.Id && Style == other.Style
        && ListEquality.Equal(Timeframes, other.Timeframes) && ListEquality.Equal(Sides, other.Sides) && ListEquality.Equal(Params, other.Params)
        && SellMirroredWith == other.SellMirroredWith && Equals(Buy, other.Buy) && Equals(Sell, other.Sell) && Equals(TimeRules, other.TimeRules) && ListEquality.Equal(Filters, other.Filters);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Schema);
        hash.Add(Id);
        hash.Add(Style);
        hash.Add(ListEquality.Hash(Timeframes));
        hash.Add(ListEquality.Hash(Sides));
        hash.Add(ListEquality.Hash(Params));
        hash.Add(Buy);
        hash.Add(Sell);
        hash.Add(SellMirroredWith);
        hash.Add(TimeRules);
        hash.Add(ListEquality.Hash(Filters));
        return hash.ToHashCode();
    }
}
