namespace XDrishti.Application.Reporting;

/// <summary>A page of the closed trades a report counts, in the order of the exits, with the evidence of the report. Never more than one page: the largest page is 1,000 trades.</summary>
/// <param name="Items">The trades of the page.</param>
/// <param name="Total">All trades the report counts.</param>
/// <param name="NextCursor">The cursor of the next page; null at the end.</param>
/// <param name="Evidence">The evidence of the report.</param>
public sealed record TradesView(IReadOnlyList<TradeLineView> Items, int Total, string? NextCursor, ReportEvidenceView Evidence);
