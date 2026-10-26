using XDrishti.Application.Reporting;

namespace XDrishti.Api.Endpoints;

/// <summary>The body of an export (spec P8-20). There is no password, no actor and no file name in it: the actor is the session, the name is made by the server.</summary>
/// <param name="Format">csv (the default and the only format).</param>
/// <param name="Query">The query of the screen the table is on, with the same field names as the GET of that screen; the page fields (limit, cursor) are ignored. There is deliberately no field for the charge-sheet confirmation.</param>
internal sealed record ExportRequest(string? Format, ReportQueryInput? Query);
