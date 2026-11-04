namespace XDrishti.Api.Lab;

/// <summary>Body of <c>PUT /api/lab/strategies/{id}/versions/{v}/registration</c> (spec CF-06, D1): the five plain texts. The actor is the signed-in owner, never a field.</summary>
internal sealed record RegistrationRequest(string? Counterparty = null, string? ParameterSigns = null, string? Applicability = null, string? ExpectedEdge = null, string? Falsifier = null);

/// <summary>Body of <c>POST /api/lab/runs/{id}/holdout-unlock</c> (spec CF-06, D5): the owner's password, typed again for this act.</summary>
internal sealed record HoldOutUnlockRequest(string? Password = null);
