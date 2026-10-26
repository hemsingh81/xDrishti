namespace XDrishti.Api.Endpoints;

/// <summary>The body of a lesson close or dismiss and of a changelog revert (P8-19). The actor, a state and any number are never part of it: a field the server does not read is ignored.</summary>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Password">The owner's password, typed again for THIS act and checked on every request (an earlier step-up window does not count and is not used up).</param>
internal sealed record OwnerMoveRequest(string? Reason, string? Password);

/// <summary>The body of a lesson link: the fix to link is a hypothesis or an experiment.</summary>
/// <param name="FixKind"><c>hypothesis</c> or <c>experiment</c>.</param>
/// <param name="FixId">The id of the fix, 1 or more.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Password">The owner's password, typed again for THIS act.</param>
internal sealed record LinkLessonRequest(string? FixKind, long? FixId, string? Reason, string? Password);

/// <summary>The body of a hypothesis move.</summary>
/// <param name="To"><c>approved</c>, <c>rejected</c>, <c>expired</c> or <c>accepted</c> (running and judged have their own commands).</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Password">The owner's password, typed again for THIS act.</param>
internal sealed record MoveHypothesisRequest(string? To, string? Reason, string? Password);
