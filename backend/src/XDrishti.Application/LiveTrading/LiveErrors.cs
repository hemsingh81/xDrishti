namespace XDrishti.Application.LiveTrading;

/// <summary>The problem codes of the live-trading records besides <c>live.invalid</c> (see <c>ComplianceRecord.InvalidCode</c>).</summary>
public static class LiveErrors
{
    public const string OwnerOnly = "live.owner_only";
    public const string CapabilityOff = "live.capability_off";
}
