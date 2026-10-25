namespace XDrishti.Application.Reporting;

/// <summary>The groups of a report for a lens, in a fixed order (a value that was not recorded last), with the evidence of the report. <see cref="NonAdditive"/> is true when the basket dimension put a trade into more than one group, so the groups must not be summed.</summary>
/// <param name="Dimensions">The dimensions grouped by, in order.</param>
/// <param name="NonAdditive">A trade appears in more than one group.</param>
/// <param name="Groups">The groups.</param>
/// <param name="Evidence">The evidence of the report.</param>
public sealed record GroupsView(IReadOnlyList<string> Dimensions, bool NonAdditive, IReadOnlyList<GroupView> Groups, ReportEvidenceView Evidence);
