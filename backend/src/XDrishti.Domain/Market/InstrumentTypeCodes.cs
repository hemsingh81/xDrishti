namespace XDrishti.Domain.Market;

/// <summary>The stored and wire spelling of <see cref="InstrumentType"/>: <c>share</c>, <c>etf</c>, <c>index</c>, <c>future</c>.</summary>
public static class InstrumentTypeCodes
{
    public const string Share = "share";
    public const string Etf = "etf";
    public const string Index = "index";
    public const string Future = "future";

    public static string ToCode(this InstrumentType type) => type switch
    {
        InstrumentType.Share => Share,
        InstrumentType.Etf => Etf,
        InstrumentType.Index => Index,
        InstrumentType.Future => Future,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static bool TryParse(string? code, out InstrumentType type)
    {
        switch (code)
        {
            case Share: type = InstrumentType.Share; return true;
            case Etf: type = InstrumentType.Etf; return true;
            case Index: type = InstrumentType.Index; return true;
            case Future: type = InstrumentType.Future; return true;
            default: type = default; return false;
        }
    }
}
