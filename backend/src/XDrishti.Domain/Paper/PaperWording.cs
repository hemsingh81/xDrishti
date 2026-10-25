namespace XDrishti.Domain.Paper;

/// <summary>The fixed sentences every paper record carries as data (spec P10-01, ADR 0116). A test pins them.</summary>
public static class PaperWording
{
    /// <summary>Stored on every entry and shown on every response: the paper trade is a replay, not an execution.</summary>
    public const string Basis = "simulated on the official 1-minute bars; no order was placed or exists";

    /// <summary>The cost basis of a trade captured with no charge sheet configured.</summary>
    public const string Gross = "gross, no charges";

    /// <summary>The honest limits shown beside every paper response.</summary>
    public static IReadOnlyList<string> Limits { get; } =
    [
        "Fills are the simulator's: touch is fill, with no queue, no latency, no partial fill and no rejection. Paper cannot validate execution.",
        "A paper trade is captured after the close from the stored official 1-minute bars; nothing is recorded live.",
        "Expired means the watch window ended; whether the ticket triggered is in this paper record.",
        "A strategy that fires twice in a session is captured once (as graded).",
    ];

    /// <summary>The cost basis text of a trade charged by the sheet <paramref name="sheetId"/>.</summary>
    public static string Net(string sheetId) => "net of the charge sheet " + sheetId;
}
