namespace XDrishti.Application.Reporting;

/// <summary>The stored runs a report may use, newest first, and whether net figures are possible (P8-06). With no stored run the list is empty, which is an ordinary answer.</summary>
/// <param name="Runs">The runs.</param>
/// <param name="Net">Whether a net report can be made.</param>
/// <param name="DefaultSetRule">How the default run set is chosen, as text.</param>
public sealed record RunsView(IReadOnlyList<RunListItemView> Runs, NetBasisView Net, string DefaultSetRule);
