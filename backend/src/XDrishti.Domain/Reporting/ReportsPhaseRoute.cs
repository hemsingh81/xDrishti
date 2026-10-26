namespace XDrishti.Domain.Reporting;

/// <summary>A route of the host's endpoint table.</summary>
/// <param name="Method">GET, POST and so on; ANY when the route names no method.</param>
/// <param name="Pattern">The route pattern, <c>/api/reports/runs</c>.</param>
public sealed record ReportsPhaseRoute(string Method, string Pattern);
