namespace XDrishti.Application.Abstractions.Market.Baskets;

/// <summary><c>Baskets:System:Derived:*</c> (BK-03): safe defaults, the refresh OFF. Off means nothing is read and nothing is written.</summary>
public interface IDerivedSettings
{
    /// <summary><c>Baskets:System:Derived:Enabled</c>, default false.</summary>
    bool Enabled { get; }

    /// <summary>The most trading days one backfill may cover (default 400).</summary>
    int MaxBackfillDays { get; }

    /// <summary>The next-ranked non-members stored beside the members (default 20).</summary>
    int ScoresKeptBeyondMembers { get; }

    /// <summary>The settings as a short JSON text for the audit trail of an owner act (no secrets are in it).</summary>
    string Snapshot();
}
