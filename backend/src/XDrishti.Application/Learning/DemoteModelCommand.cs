using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Learning;

/// <summary>
/// Moves the champion to challenger (shadow): the one registry move the rollback path may ask for (spec CF-10 D8, part 10d-1, ADR 0220). It carries no target role, so it cannot name a champion, and no facts. The handler builds the designation with <see cref="Domain.Learning.ModelRole.Challenger"/>
/// and no other role. It lowers reliance on ML; raising it again (re-promotion) is the owner's act and a different command.
/// </summary>
/// <param name="ModelId">The champion to demote.</param>
/// <param name="Reason">Why, with the trigger and the numbers (at most 500 characters).</param>
/// <param name="Actor"><c>owner:...</c> or <c>auto</c>.</param>
/// <param name="Evidence">Evidence ids for the changelog row (the rollback proposal and, for <c>auto</c>, the switch row).</param>
public sealed record DemoteModelCommand(long ModelId, string Reason, string Actor, IReadOnlyList<string> Evidence) : ICommand;
