using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.LearningPhase;

/// <summary>The report with its two renderings; both come from the same report.</summary>
public sealed record EventsPhaseCheckView(EventsPhaseReport Report, string Text, string Json);
