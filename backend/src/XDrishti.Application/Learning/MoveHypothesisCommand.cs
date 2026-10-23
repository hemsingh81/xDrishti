using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Moves a hypothesis to Approved, Rejected, Expired or Accepted (P6-10, ADR 0076). Starting and judging have their own commands because they attach a run and an experiment. Accepting records the verdict only: it applies nothing.
/// </summary>
/// <param name="Id">The hypothesis id.</param>
/// <param name="To">The wanted state.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Actor">Who moves it; <c>cli:xd</c> and <c>owner:...</c> are the owner.</param>
/// <param name="Autonomy">The autonomy level in force (supervised unless stated; it is not stored yet).</param>
public sealed record MoveHypothesisCommand(long Id, HypothesisState To, string Reason, string Actor, AutonomyLevel Autonomy = AutonomyLevel.Supervised) : ICommand;
