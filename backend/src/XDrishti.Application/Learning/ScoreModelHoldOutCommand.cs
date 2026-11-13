using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Learning;

/// <summary>
/// The owner's act of scoring a model on the locked hold-out (CF-10e part 3, spec D5, ADR 0285): claims the model family's one fresh look at the block in force (insert-only, burned even if the pass is abandoned) and scores the model there once. The actor must be <c>owner:...</c>, taken from the
/// signed-in session by the endpoint (which also asks for a fresh password), never from a body. Places no order and changes no setting.
/// </summary>
/// <param name="ModelId">The registry id.</param>
/// <param name="Actor">The owner, <c>owner:...</c>.</param>
public sealed record ScoreModelHoldOutCommand(long ModelId, string Actor) : ICommand;
