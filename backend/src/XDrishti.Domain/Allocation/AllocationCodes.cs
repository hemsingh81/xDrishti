namespace XDrishti.Domain.Allocation;

/// <summary>Stable codes of the per-account allocation (spec P7-07, ADR 0089). One code per decision.</summary>
public static class AllocationCodes
{
    /// <summary>The account received a quantity.</summary>
    public const string Allocated = "allocated";

    /// <summary>The account does not have the Trading role: excluded, never allocated.</summary>
    public const string NotTrading = "account_not_trading";

    /// <summary>The account is not in paper mode: excluded, never allocated (owner Q2, 2026-10-23).</summary>
    public const string NotPaper = "account_not_paper";

    /// <summary>The account is not a manual-live account: it is not in the live pool (spec P11-04).</summary>
    public const string NotLive = "account_not_live";

    /// <summary>The same account id was supplied more than once: every copy is excluded.</summary>
    public const string DuplicateAccount = "duplicate_account";

    /// <summary>The ticket's side, prices, quantity or lot size is missing or unusable: nothing is allocated to anyone.</summary>
    public const string UnavailableTicket = "unavailable_ticket";

    /// <summary>A number of the account that the sizing needs is missing or unusable: nothing is allocated to it.</summary>
    public const string UnavailableAccount = "unavailable_account";

    /// <summary>The account already holds the symbol.</summary>
    public const string AlreadyHeld = "already_held";

    /// <summary>The account's own limits allow less than one lot; <see cref="AccountAllocation.Binding"/> names the tightest.</summary>
    public const string BelowOneLot = "below_one_lot";

    /// <summary>The account could take a quantity, but the ticket's quantity went to accounts with more room.</summary>
    public const string PoolExhausted = "pool_exhausted";

    /// <summary>Binding limit: risk of one trade (percent of equity over the stop distance).</summary>
    public const string RiskPerTrade = "risk_per_trade";

    /// <summary>Binding limit: capital in one trade (percent of equity over the entry price).</summary>
    public const string CapitalPerTrade = "capital_per_trade";

    /// <summary>Binding limit: the funds the account can use.</summary>
    public const string Funds = "funds";

    /// <summary>Binding limit: headroom of the total open risk.</summary>
    public const string ExposureHeadroom = "exposure_headroom";
}
