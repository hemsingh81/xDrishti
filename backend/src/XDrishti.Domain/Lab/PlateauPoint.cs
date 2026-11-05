namespace XDrishti.Domain.Lab;

/// <summary>One point of a parameter's curve for the plateau gate (CF-07, D4): a value and the figures of its plain run, or the reason it has none. The declared value is a point too (the base run).</summary>
/// <param name="Value">The parameter value.</param>
/// <param name="Trades">Trades of the run.</param>
/// <param name="MeanR">Mean net R of the run; null when the run gave no trade or was refused.</param>
/// <param name="Error">Why the engine refused the run; null otherwise.</param>
public sealed record PlateauPoint(double Value, int Trades, double? MeanR, string? Error)
{
    /// <summary>True when the point has a finite mean and no error.</summary>
    public bool IsValid => Error is null && MeanR is { } m && double.IsFinite(m);
}
