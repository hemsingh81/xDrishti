using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>The P7 phase check (spec P7-18, ADR 0097): the acceptance criteria judged from stored records. Read only.</summary>
/// <param name="Through">The last planned session of the window; null for the newest planned session whose nightly run is due.</param>
public sealed record EvaluatePlanPhaseCheckQuery(DateOnly? Through) : IQuery<Result<PlanPhaseCheckView>>;

/// <summary>The phase check as the verb prints it and the endpoint returns it.</summary>
/// <param name="Report">The criteria, the sessions, the setup and the limits.</param>
/// <param name="Text">The printable report, limits included.</param>
public sealed record PlanPhaseCheckView(PlanPhaseReport Report, string Text);
