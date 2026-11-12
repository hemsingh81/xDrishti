using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The owner's local designation (<c>xd learn designate</c>, spec P6-07), the one use case behind that verb (CF-10 part 10d-1 review, ADR 0220). It is <see cref="DesignateModelCommand"/> with one extra refusal: a model that has a rollback proposal, in any state, is not named champion
/// from the command line; it comes back only through the promote route (fresh password, the predicate without the switch). Naming a champion when no model has ever been champion checks the champion rule alone (the first champion); once any model has been champion, a champion is named only through <see cref="PromoteChallengerCommand"/> (CF-10e D6, ADR 0281), and the architecture tripwire of ADR 0220 stays green because of it.
/// </summary>
/// <param name="ModelId">The registry id.</param>
/// <param name="Role">The role.</param>
/// <param name="Reason">Why, at most 500 characters.</param>
/// <param name="Actor">Who asks (the command line's actor).</param>
public sealed record OwnerDesignateModelCommand(long ModelId, ModelRole Role, string Reason, string Actor) : ICommand;
