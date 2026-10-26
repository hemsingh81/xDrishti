namespace XDrishti.Api.Endpoints;

/// <summary>The body of a capability switch. The owner is never part of it: the actor comes from the signed-in session.</summary>
/// <param name="Enabled">True to switch the capability ON (it is off by default), false to switch it OFF.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Password">The owner's password, re-entered for THIS switch. Needed to switch ON and checked on every request (an earlier step-up window does not count and is not used up); not needed to switch OFF.</param>
internal sealed record ChangeCapabilityRequest(bool Enabled, string? Reason, string? Password);
