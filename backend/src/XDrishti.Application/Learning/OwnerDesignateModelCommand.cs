using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The owner's local designation (<c>xd learn designate</c>, spec P6-07), the one use case behind that verb (CF-10 part 10d-1 review, ADR 0220). It is <see cref="DesignateModelCommand"/> with one extra refusal: a model that has a rollback proposal, in any state, is not named champion
/// from the command line; it comes back only through the promote route (fresh password, the predicate without the switch). <b>OWNER DECISION NEEDED:</b> naming a champion this way still checks only the champion rule (<c>ChampionRule</c>), not the promotion rule of D7, because the
/// evidence sources of D7 do not exist yet and the system would otherwise be unable to name any champion; an architecture tripwire fails the day a source exists while this verb can still name a champion without going through the promotion handler.
/// </summary>
/// <param name="ModelId">The registry id.</param>
/// <param name="Role">The role.</param>
/// <param name="Reason">Why, at most 500 characters.</param>
/// <param name="Actor">Who asks (the command line's actor).</param>
public sealed record OwnerDesignateModelCommand(long ModelId, ModelRole Role, string Reason, string Actor) : ICommand;
