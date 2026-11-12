namespace XDrishti.Api.Endpoints;

/// <summary>The body of a history owner act (CF-04e). The owner is never part of it: the actor comes from the signed-in session.</summary>
/// <param name="Password">The owner's password, re-entered for THIS act. It is checked on every request after the switch (an earlier step-up window does not count and is not used up); wrong ones count toward the sign-in lockout.</param>
internal sealed record HistoryActRequest(string? Password);
