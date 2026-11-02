using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>What protocol F classifies (P15-10): the trades of the unfiltered stored runs with the calendar's answer at each decision instant, and the identity of what the answers came from.</summary>
/// <param name="Observations">One per distinct trade key, ordered by date then key.</param>
/// <param name="CalendarSha256">The hash of the calendar snapshot read.</param>
/// <param name="LagMinutes">The lag applied.</param>
/// <param name="RunKeys">The runs used, sorted.</param>
/// <param name="Engines">The distinct engine versions of those runs, sorted.</param>
/// <param name="RunsSkipped">Runs left out because they used the event filter themselves.</param>
public sealed record FilterPopulation(IReadOnlyList<FilterObservation> Observations, string CalendarSha256, int LagMinutes, IReadOnlyList<string> RunKeys, IReadOnlyList<string> Engines, int RunsSkipped);
