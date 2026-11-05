namespace XDrishti.Application.Lab;

/// <summary>One point of a parameter's curve in the plateau gate (CF-07, D4): the declared value is marked.</summary>
/// <param name="Value">The parameter value.</param>
/// <param name="Declared">True for the value the strategy declares (the base run).</param>
/// <param name="Trades">Trades of its in-sample run.</param>
/// <param name="MeanR">Net expectancy of the run; null when it gave no trade or was refused.</param>
/// <param name="Retention">Its mean over the base mean; null when either cannot be read or the base is not above 0.</param>
/// <param name="Error">Why the engine refused the run; null otherwise.</param>
public sealed record PlateauPointView(double Value, bool Declared, int Trades, double? MeanR, double? Retention, string? Error);

/// <summary>
/// The plateau reading of one swept parameter (CF-07, D4): the two points next to the declared value must each keep the retention of the base and the median over the valid points must be above 0. <see cref="Reading"/> is <c>plateau</c>, <c>spike</c>,
/// <c>no_base_edge</c> or <c>inconclusive</c>; <see cref="Status"/> is <c>pass</c>, <c>fail</c> or <c>unavailable</c>. The centre is reported, never applied.
/// </summary>
/// <param name="Param">The parameter name.</param>
/// <param name="Declared">The declared value.</param>
/// <param name="ValidPoints">Valid points, the declared value included.</param>
/// <param name="BaseMeanR">Net expectancy at the declared value.</param>
/// <param name="LowerValue">The swept value just below the declared one.</param>
/// <param name="LowerRetention">Its mean over the base mean.</param>
/// <param name="UpperValue">The swept value just above the declared one.</param>
/// <param name="UpperRetention">Its mean over the base mean.</param>
/// <param name="Median">Median net expectancy over the valid points.</param>
/// <param name="CentreValue">The value maximizing the mean of itself and its two neighbours.</param>
/// <param name="CentreMeanR">That smoothed mean.</param>
/// <param name="Reading">plateau, spike, no_base_edge or inconclusive.</param>
/// <param name="Status">pass, fail or unavailable.</param>
/// <param name="Reason">One sentence for the reading.</param>
/// <param name="Points">All points in value order.</param>
public sealed record PlateauParameterView(
    string Param,
    double Declared,
    int ValidPoints,
    double? BaseMeanR,
    double? LowerValue,
    double? LowerRetention,
    double? UpperValue,
    double? UpperRetention,
    double? Median,
    double? CentreValue,
    double? CentreMeanR,
    string Reading,
    string Status,
    string Reason,
    IReadOnlyList<PlateauPointView> Points);

/// <summary>
/// The plateau gate of a Lab verdict (CF-07 part C, ADR 0210), read from the stored points of the in-sample sweep (no new simulation): <see cref="Status"/> is <c>pass</c> only when every swept parameter sits on a plateau, <c>fail</c> when one does not, and
/// <c>unavailable</c> (never a pass) when one has no reading or the strategy sweeps none. Report-only beside PASS/MARGINAL/FAIL unless the owner turns <c>Plateau:Enforce</c> on.
/// </summary>
/// <param name="Mode">The mode when it was computed.</param>
/// <param name="Enforce">Whether the gate could cap the outcome.</param>
/// <param name="Settings">The text of the settings that produced the figures.</param>
/// <param name="SettingsHash">SHA-256 of <paramref name="Settings"/>.</param>
/// <param name="Retention">The share of the base each neighbour must keep (the threshold).</param>
/// <param name="MinPoints">The fewest valid points of a parameter for a reading.</param>
/// <param name="Comparison">How a neighbour's retention is read against the threshold (<c>&gt;=</c>).</param>
/// <param name="Status">pass, fail or unavailable.</param>
/// <param name="Reason">One sentence for the reading.</param>
/// <param name="Capped">True when the outcome was capped at MARGINAL by the enforced gate.</param>
/// <param name="Basis">What the figure is made over: the in-sample sweep, its window and the exit.</param>
/// <param name="Parameters">One reading per swept parameter.</param>
public sealed record PlateauView(string Mode, bool Enforce, string Settings, string SettingsHash, double Retention, int MinPoints, string Comparison, string Status, string Reason, bool Capped, string Basis, IReadOnlyList<PlateauParameterView> Parameters);
