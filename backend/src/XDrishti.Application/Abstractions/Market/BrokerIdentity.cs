namespace XDrishti.Application.Abstractions.Market;

/// <summary>A broker's id of an instrument: exchange segment (<c>NSE_EQ</c>, <c>IDX_I</c>, <c>NSE_FNO</c>) and the broker's own id (Dhan <c>SECURITY_ID</c>).</summary>
public sealed record BrokerIdentity(string Segment, string BrokerId);
