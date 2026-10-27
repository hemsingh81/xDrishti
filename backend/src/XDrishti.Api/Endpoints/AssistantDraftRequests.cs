namespace XDrishti.Api.Endpoints;

/// <summary>The body of <c>POST /api/assistant/strategy-drafts</c>: the owner's idea in plain text and, optionally, a basket the idea is about. The actor is never in it.</summary>
internal sealed record StartDraftRequest(string? Idea, Guid? BasketId);

/// <summary>
/// The body of <c>POST /api/assistant/strategy-drafts/{id}/confirm</c> (spec P9-07 AC 7): the owner's edited text (absent: the stored draft's), the universe of the quick test (a basket or symbols; the owner picks), whether to run it, and the owner's password for THIS confirm.
/// The actor is never in it.
/// </summary>
internal sealed record ConfirmDraftRequest(string? Text, Guid? BasketId, IReadOnlyList<string>? Symbols, bool Run, string? Password);
