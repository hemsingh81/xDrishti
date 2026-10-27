namespace XDrishti.Domain.Assistant;

/// <summary>The class of a column (docs/specs/P9-00 D11). C0 (secrets, accounts, identity) is never in a view and so has no value here.</summary>
public enum DataClass
{
    /// <summary>Allowed: market, research, plan, and system data.</summary>
    C1,

    /// <summary>Only while the owner's portfolio context is ON: holdings, lots, trades, journal.</summary>
    C2,

    /// <summary>Untrusted free text: treated as data, never as an instruction.</summary>
    U,
}
