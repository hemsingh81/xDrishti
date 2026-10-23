namespace XDrishti.Domain.Learning;

/// <summary>
/// Who a cell is (doc 09 layer 1): strategy, timeframe, side, style, exit policy and regime (<c>*</c> until P6-02). <see cref="Key"/> is the text <c>strategy|tf|side|style|exit|regime</c> that the table and the API use.
/// </summary>
/// <param name="Strategy">The strategy id.</param>
/// <param name="Timeframe">The evaluation timeframe code, for example <c>15m</c>.</param>
/// <param name="Side">The side, <c>BUY</c> or <c>SELL</c>.</param>
/// <param name="Style">The style, <c>intraday</c> or <c>swing</c>.</param>
/// <param name="Exit">The label of the exit policy.</param>
/// <param name="Regime"><c>*</c> for all regimes, or the regime code.</param>
public sealed record CellId(string Strategy, string Timeframe, string Side, string Style, string Exit, string Regime = "*")
{
    /// <summary>The longest part, in characters; the table columns are this long or longer.</summary>
    public const int MaxPartLength = 64;

    /// <summary><c>strategy|tf|side|style|exit|regime</c>.</summary>
    public string Key => string.Join('|', Strategy, Timeframe, Side, Style, Exit, Regime);

    /// <summary>Reads <c>strategy|tf|side|style|exit|regime</c> back; null when it is not six valid parts.</summary>
    public static CellId? TryParse(string? key)
    {
        var parts = key?.Split('|');
        if (parts is not { Length: 6 })
        {
            return null;
        }

        var id = new CellId(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5]);
        return id.Problems().Count == 0 ? id : null;
    }

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
