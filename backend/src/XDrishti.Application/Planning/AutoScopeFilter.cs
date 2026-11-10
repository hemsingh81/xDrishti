using XDrishti.Application.Abstractions.Planning;

namespace XDrishti.Application.Planning;

/// <summary>The one filter of spec AU-16: keeps the candidates whose instrument is in the scope. It reads no gate and changes no ranking; a candidate whose instrument is not known is not in any scope.</summary>
internal static class AutoScopeFilter
{
    public static IReadOnlyList<TicketCandidate> Apply(IReadOnlyList<TicketCandidate> candidates, ResolvedAutoScope? scope) =>
        scope is null ? candidates : [.. candidates.Where(c => c.InstrumentId is { } id && scope.InstrumentIds.Contains(id))];
}
