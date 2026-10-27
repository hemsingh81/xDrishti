namespace XDrishti.Domain.Assistant.Text;

/// <summary>The pinned strings of the briefing (spec P9-06): code adds them, the model never writes them.</summary>
public static class BriefingWording
{
    /// <summary>Ends every daily text.</summary>
    public const string DailyFooter = "This text only restates stored records. It is not an order or an approval, and plans are provisional and simulated on plan prices.";

    /// <summary>Ends every weekly text (added by code, never by the model).</summary>
    public const string WeeklyFooter = "Nothing was applied: every item above is a proposal; applying any of it is the owner's own act.";

    /// <summary>Acronyms and words a text may use besides the symbols and names of the facts.</summary>
    public static IReadOnlyList<string> AllowedAcronyms { get; } = ["NSE", "IST", "ATR", "VIX", "ORB", "VWAP", "EMA", "RSI", "SL", "BUY", "SELL", "INTRADAY", "SWING"];

    /// <summary>Claims a text may never make.</summary>
    public static IReadOnlyList<string> NeverSay { get; } = ["profit guaranteed", "risk-free", "certain"];
}
