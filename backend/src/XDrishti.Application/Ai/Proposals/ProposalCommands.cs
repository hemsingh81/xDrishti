using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai.Proposals;

/// <summary>Generate proposals once (spec P9-08 AC 1 to 8): the service or the verb asks. <paramref name="Kind"/> and <paramref name="Cell"/> narrow the pack; <paramref name="DryRun"/> evaluates and stores nothing.</summary>
public sealed record GenerateProposalsCommand(string? Kind, string? Cell, bool DryRun, string Actor) : ICommand;

/// <summary>The owner accepts a proposal (spec P9-08 AC 9). The actor is the signed-in owner, never from a body; the endpoint has verified the fresh password.</summary>
public sealed record AcceptProposalCommand(long Id, string Actor) : ICommand;

/// <summary>The owner rejects a proposal (AC 10); the reason is optional, at most 300 characters.</summary>
public sealed record RejectProposalCommand(long Id, string? Reason, string Actor) : ICommand;

/// <summary>The auto option (AC 10a): asks whether the just-stored hypothesis proposals may be accepted without the owner. Made only by the generation job, never from a body or a model argument.</summary>
internal sealed record AutoAcceptProposalsCommand(IReadOnlyList<long> Ids) : ICommand;

/// <summary>What the auto option did: how many it accepted and the one-word reason for the first one it left to the owner.</summary>
internal sealed record AutoAcceptOutcome(int Accepted, string? LeftReason);

/// <summary>One proposal with its decision.</summary>
public sealed record GetProposalQuery(long Id) : IQuery<Result<ProposalView>>;

/// <summary>The proposals, newest first; a state (pending, accepted, rejected, expired) and a kind narrow the list; 1 to 100, default 50.</summary>
public sealed record ListProposalsQuery(string? State, string? Kind, int Limit = ListProposalsQuery.DefaultLimit) : IQuery<Result<IReadOnlyList<ProposalView>>>
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;
}

/// <summary>Whether the owner's switch for accepting proposals is on now (read before the password is asked, so that a switched-off act never counts toward the lockout).</summary>
public sealed record ProposalAcceptAllowedQuery : IQuery<bool>;
