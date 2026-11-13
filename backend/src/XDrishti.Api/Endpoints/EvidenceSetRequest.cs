namespace XDrishti.Api.Endpoints;

/// <summary>The body of the evidence-set replacement. The owner is never part of it: the actor comes from the signed-in session.</summary>
/// <param name="Versions">The strategy version hashes (64 lower-case hex characters each, stored versions) that make the set: 1 to 500, distinct.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Password">The owner's password, re-entered for THIS change; checked on every request, wrong ones count toward the sign-in lockout.</param>
internal sealed record EvidenceSetRequest(IReadOnlyList<string>? Versions, string? Reason, string? Password);
