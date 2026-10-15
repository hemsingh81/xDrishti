namespace XDrishti.Application.Portfolio.Attention;

/// <summary>
/// What the owner can do about an item (ADR 0027), at most two per item. <c>Kind</c> <c>route</c>: <c>Target</c> is an in-app path with its query (<c>/accounts?account=…&amp;tab=connection</c>) to navigate to. <c>Kind</c> <c>command</c>: <c>Target</c> is an existing
/// endpoint as <c>METHOD /api/path</c> (<c>POST /api/accounts/{id}/sync/broker-history</c>), which the browser calls with its session and CSRF token like any other call of the app - no new surface.
/// </summary>
public sealed record AttentionAction(string Label, string Kind, string Target);

/// <summary>Codes of <see cref="AttentionAction.Kind"/>.</summary>
public static class AttentionActionKinds
{
    public const string Route = "route";

    public const string Command = "command";
}

/// <summary>Codes of the severity of an attention item.</summary>
public static class AttentionSeverities
{
    /// <summary>Something stopped data from flowing: the Dhan login, a sync that keeps failing.</summary>
    public const string Error = "error";

    /// <summary>A decision that affects figures: holdings to review, trades that do not explain a holding, securities with no match.</summary>
    public const string Warning = "warning";

    /// <summary>A suggestion.</summary>
    public const string Info = "info";

    public static int Rank(string severity) => severity switch
    {
        Error => 0,
        Warning => 1,
        _ => 2,
    };
}

/// <summary>Codes of the kind of an attention item (the <c>kind</c> of a portfolio notice).</summary>
public static class AttentionKinds
{
    public const string ReconnectDhan = "reconnect_dhan";

    public const string SyncFailing = "sync_failing";

    public const string HoldingsNeedReview = "holdings_need_review";

    public const string TradesUnexplained = "trades_unexplained";

    public const string DividendsPending = "dividends_pending";

    public const string SecuritiesUnmatched = "securities_unmatched";
}

/// <summary>An item as the derivation produces it, before it is remembered: the id includes the situation. <c>AccountId</c> is the account it is about.</summary>
internal sealed record AttentionItem(string Id, string Kind, string Severity, string Title, string Text, IReadOnlyList<AttentionAction> Actions, Guid? AccountId);
