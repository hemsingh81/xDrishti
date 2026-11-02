using XDrishti.Domain.Selection;

namespace XDrishti.Application.Abstractions.Selection;

/// <summary>
/// The newest stored protocol-F verdict on the event-risk filter for a rule version and the window N of the effective switch (implemented by <c>EventFilterVerdictReader</c>, P15-10). With no stored verdict for that N it answers null (unavailable), so the auto path cannot fire. A verdict is KEEP, DROP, INCONCLUSIVE or NOT_EVALUABLE
/// with a reference to its evidence block; null is unavailable and is never read as KEEP.
/// </summary>
public interface IEventFilterVerdict
{
    ValueTask<EventFilterVerdict?> NewestAsync(string ruleVersion, CancellationToken cancellationToken);
}
