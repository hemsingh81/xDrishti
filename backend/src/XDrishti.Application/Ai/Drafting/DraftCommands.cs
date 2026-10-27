using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Ai.Drafting;

/// <summary>The owner types an idea (spec P9-07): checks the limits, takes the model slot, reserves an id and starts the job. The actor is the signed-in owner, never from a body.</summary>
public sealed record StartDraftCommand(string Idea, Guid? BasketId, string Actor) : ICommand;

/// <summary>The body of the job (internal: only the start handler makes it).</summary>
internal sealed record DraftStrategyCommand(long Id, string Idea, Guid? BasketId, string Actor) : ICommand;

/// <summary>The owner's confirm (spec P9-07 AC 7): store the text as a normal library version and, when asked, start a quick test. <paramref name="Text"/> is the owner's edited text, or null for the stored draft's.</summary>
public sealed record ConfirmDraftCommand(long Id, string? Text, Guid? BasketId, IReadOnlyList<string>? Symbols, bool Run, string Actor) : ICommand;

/// <summary>The auto option (spec P9-07 D9): asks whether the just-stored draft may be confirmed for a quick test without the owner. Made only by the drafting job, never from a body or a model argument.</summary>
internal sealed record AutoDraftCommand(long DraftId) : ICommand;

/// <summary>What the auto option did: <paramref name="Started"/> when it confirmed the draft, else the one-word reason it left the draft for the owner.</summary>
internal sealed record AutoDraftOutcome(bool Started, string Reason);

/// <summary>The owner discards a draft: a decision row, nothing stored as a strategy.</summary>
public sealed record DiscardDraftCommand(long Id, string Actor) : ICommand;

/// <summary>One draft with its decision and the linked run summary.</summary>
public sealed record GetDraftQuery(long Id) : IQuery<XDrishti.Domain.Common.Result<DraftView>>;

/// <summary>The newest drafts (limit 1 to 50).</summary>
public sealed record ListDraftsQuery(int Limit = ListDraftsQuery.DefaultLimit) : IQuery<XDrishti.Domain.Common.Result<IReadOnlyList<DraftView>>>
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;
}

/// <summary>Whether the owner's switch for confirming drafts is on now (read before the password is asked, so that a switched-off act never counts toward the lockout).</summary>
public sealed record DraftConfirmAllowedQuery : IQuery<bool>;
