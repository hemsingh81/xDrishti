namespace XDrishti.Application.LiveTrading;

/// <summary>The problem codes of the live-trading records besides <c>live.invalid</c> (see <c>ComplianceRecord.InvalidCode</c>).</summary>
public static class LiveErrors
{
    public const string OwnerOnly = "live.owner_only";
    public const string CapabilityOff = "live.capability_off";
    public const string Stale = "live.stale";
    public const string AccountNotFound = "live.account_not_found";
    public const string NotFound = "live.not_found";
    public const string Invalid = "live.invalid";
}
