namespace XDrishti.Domain.Learning;

/// <summary>
/// Who a cell is (doc 09 layer 1): strategy, timeframe, side, style, exit policy, regime (<c>*</c> until P6-02), universe (<c>*</c> until BK-11) and expression (<c>cash</c> until P14-08). <see cref="Key"/> is the text
/// <c>strategy|tf|side|style|exit|regime[|universe[|expression]]</c> that the table and the API use, written with the fewest trailing parts (ADR 0193): a cash cell with universe <c>*</c> reads as the six-part text it always had.
/// </summary>
/// <param name="Strategy">The strategy id.</param>
/// <param name="Timeframe">The evaluation timeframe code, for example <c>15m</c>.</param>
/// <param name="Side">The side, <c>BUY</c> or <c>SELL</c>.</param>
/// <param name="Style">The style, <c>intraday</c> or <c>swing</c>.</param>
/// <param name="Exit">The label of the exit policy.</param>
/// <param name="Regime"><c>*</c> for all regimes, or the regime code.</param>
/// <param name="Universe">The seventh part (BK-11, ADR 0261): <c>*</c> for all shares or a liquidity tier, <c>liq_high</c>, <c>liq_mid</c> or <c>liq_low</c>.</param>
/// <param name="Expression">The eighth part: how the signal is expressed; <see cref="Learning.Expression.Cash"/> for every cell that has no eighth part.</param>
public sealed record CellId(string Strategy, string Timeframe, string Side, string Style, string Exit, string Regime = "*", string Universe = CellId.AllUniverses, Expression Expression = Expression.Cash)
{
    /// <summary>The longest part, in characters; the table columns are this long or longer.</summary>
    public const int MaxPartLength = 64;

    /// <summary>The universe of every cell until BK-11 widens the list.</summary>
    public const string AllUniverses = "*";

    /// <summary>The liquidity tiers a cell may be split by (BK-11): the only universe values other than <c>*</c>. One dimension of three: a second universe-like part is not allowed (<see cref="CellExplosionGuard"/>).</summary>
    public static readonly IReadOnlyList<string> Tiers = ["liq_high", "liq_mid", "liq_low"];

    /// <summary>The universes a key may name: <c>*</c> and the tiers.</summary>
    private static readonly string[] KnownUniverses = [AllUniverses, .. Tiers];

    /// <summary>True for a cash cell: the only kind that selection, planning, paper, live and execution may read (ADR 0193).</summary>
    public bool IsCash => Expression == Expression.Cash;

    /// <summary>The key with the fewest trailing parts: six for universe <c>*</c> and cash, seven for another universe and cash, eight for any other expression.</summary>
    public string Key =>
        Expression != Expression.Cash ? string.Join('|', Strategy, Timeframe, Side, Style, Exit, Regime, Universe, Expression.Code())
        : Universe != AllUniverses ? string.Join('|', Strategy, Timeframe, Side, Style, Exit, Regime, Universe)
        : string.Join('|', Strategy, Timeframe, Side, Style, Exit, Regime);

    /// <summary>
    /// Reads a key back; null when it is not 6, 7 or 8 valid parts, or not canonical. A seventh part must be a known universe other than <c>*</c>, an eighth part a known expression other than <c>cash</c>: so two texts never name
    /// one cell.
    /// </summary>
    public static CellId? TryParse(string? key)
    {
        var parts = key?.Split('|');
        if (parts is null || parts.Length is < 6 or > 8)
        {
            return null;
        }

        var universe = AllUniverses;
        var expression = Expression.Cash;
        if (parts.Length >= 7)
        {
            universe = parts[6];
            if (Array.IndexOf(KnownUniverses, universe) < 0 || (parts.Length == 7 && universe == AllUniverses))
            {
                return null;
            }
        }

        if (parts.Length == 8)
        {
            if (Learning.ExpressionNames.Parse(parts[7]) is not { } parsed || parsed == Expression.Cash)
            {
                return null;
            }

            expression = parsed;
        }

        var id = new CellId(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], universe, expression);
        return id.Problems().Count == 0 ? id : null;
    }

    /// <summary>
    /// The cell this one is shrunk toward, one step up the single chain from the narrowest cell to the all-shares cell (BK-11): a regime cell first loses its regime (as before: regime, then universe), then a tiered cell loses its universe; a cell with both
    /// is <c>(regime, universe)</c>, then <c>(*, universe)</c>, then <c>(*, *)</c>. The expression part never changes (an option cell is not a child of a cash cell). Null for a cell with regime and universe <c>*</c>.
    /// </summary>
    public CellId? Parent() => Regime != "*" ? this with { Regime = "*" } : Universe != AllUniverses ? this with { Universe = AllUniverses } : null;

    /// <summary>The problems of the parts: each is required, short and free of the separator.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        Check(problems, "strategy", Strategy);
        Check(problems, "timeframe", Timeframe);
        Check(problems, "side", Side);
        Check(problems, "style", Style);
        Check(problems, "exit", Exit);
        Check(problems, "regime", Regime);
        if (Array.IndexOf(KnownUniverses, Universe) < 0)
        {
            problems.Add("universe is not a known universe (* or a liquidity tier)");
        }

        if (!Enum.IsDefined(Expression))
        {
            problems.Add("expression is not a known expression");
        }

        return problems.AsReadOnly();
    }

    private static void Check(List<string> problems, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxPartLength || value.Contains('|', StringComparison.Ordinal))
        {
            problems.Add($"{name} is required, at most {MaxPartLength} characters and without '|'");
        }
    }
}
