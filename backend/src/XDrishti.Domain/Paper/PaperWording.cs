namespace XDrishti.Domain.Paper;

/// <summary>The fixed sentences every paper record carries as data (spec P10-01, ADR 0116). A test pins them.</summary>
public static class PaperWording
{
    /// <summary>Stored on every entry and shown on every response: the paper trade is a replay, not an execution.</summary>
    public const string Basis = "simulated on the official 1-minute bars; no order was placed or exists";

    /// <summary>The cost basis of a trade captured with no charge sheet configured.</summary>
    public const string Gross = "gross, no charges";

    /// <summary>The basis of a ledger result when every closed trade was charged by a stored sheet (spec P10-02, criterion 5): never called net, because no view here can read the owner's confirmation (ISS-25).</summary>
    public const string AfterStoredSheet = "after the stored sheet; the sheet is not confirmed as the owner's in this view (ISS-25)";

    /// <summary>The basis when only some closed trades carry a stored sheet's charges.</summary>
    public static string Mixed(int charged, int closed) => $"mixed: {charged} of {closed} closed trades carry a stored sheet's charges, the rest are gross; the sheet is not confirmed as the owner's in this view (ISS-25)";

    /// <summary>The plan note when the book is the paper ledger (kept short: the plan's basis column holds 500 characters).</summary>
    public const string BookNote = "Open book: the paper ledger (simulated trades only; positions held elsewhere are not counted).";

    /// <summary>The plan note when a paper account has no capital figure.</summary>
    public const string NoCapitalNote = "A paper account without capital is not in the book; its limits are unknown.";

    /// <summary>The plan note when the paper book cannot be trusted (spec P10-02, criterion 8).</summary>
    public static string BookUnavailable(string why) => "paper book unavailable: " + why;

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
