namespace XDrishti.Api.Endpoints;

/// <summary>The body of <c>POST /api/assistant/proposals/{id}/accept</c> (spec P9-08 AC 9): the owner's password for THIS accept. The actor is never in it.</summary>
internal sealed record AcceptProposalRequest(string? Password);

/// <summary>The body of <c>POST /api/assistant/proposals/{id}/reject</c> (AC 10): an optional reason of at most 300 characters.</summary>
internal sealed record RejectProposalRequest(string? Reason);
