using XDrishti.Domain.Assistant.Text;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>One historical plan of the briefing set (B): the stored facts a briefing is written from and what a good text must keep (spec P9-09 AC 5).</summary>
public sealed record PlanCase(string Id, string Description, BriefingFacts Facts, IReadOnlyList<string> ExpectTickets, IReadOnlyList<string> ExpectFlags);
