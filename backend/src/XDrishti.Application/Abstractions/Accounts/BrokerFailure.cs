namespace XDrishti.Application.Abstractions.Accounts;

/// <summary>Why a broker call failed. BrokerCode is the broker's own error code (for example DH-901, UDAPI100069) when it gave a well-formed one; never message text.</summary>
public sealed record BrokerFailure(BrokerFailureKind Kind, string? BrokerCode = null);
