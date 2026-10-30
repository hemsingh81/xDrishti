namespace XDrishti.Api.Endpoints;

/// <summary>The owner's button. <c>scope</c> is system or account (then <c>accountId</c> is required); <c>level</c> is entries, cancel or exit_all (exit_all needs the step-up window). The actor is never taken from the body.</summary>
internal sealed record StartHaltRequest(string? Scope, Guid? AccountId, string? Level, string? Reason, bool? BrokerKillSwitch);

/// <summary>The owner's release of a halt: a reason and the password, checked afresh for this act.</summary>
internal sealed record ReleaseHaltRequest(string? Reason, string? Password);
