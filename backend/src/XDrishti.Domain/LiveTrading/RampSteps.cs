namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// The three steps of the risk ramp (spec P11-04, ADR 0148): S1 25 %, S2 50 %, S3 100 % of the profile's risk. The ceilings are the fixed factors; the configured factors (<c>Live:Ramp:StepFactors</c>) may be lower, never higher, and the database
/// refuses an allocation whose scale is above the ceiling of its step. S3 is never reached without the owner's recorded exit confirmation (the table enforces it).
/// </summary>
public static class RampSteps
{
    public const int First = 1;
    public const int Third = 3;

    /// <summary>The highest scale each step may carry (S1, S2, S3).</summary>
    public static IReadOnlyList<double> Ceilings { get; } = [0.25, 0.5, 1.0];

    public static bool IsStep(int step) => step is >= First and <= Third;

    public static double Ceiling(int step) => Ceilings[step - 1];
}
