namespace XDrishti.Domain.Accounts;

/// <summary>Whether a broker callback may use a pending sign-in (<see cref="PendingConnect.Check"/>).</summary>
public enum PendingConnectCheck
{
    Open,

    /// <summary>Started by another session: never usable from here.</summary>
    OtherSession,

    /// <summary>Used already (or replaced by a newer sign-in).</summary>
    Used,

    Expired,
}
