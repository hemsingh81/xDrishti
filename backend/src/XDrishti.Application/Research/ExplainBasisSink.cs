namespace XDrishti.Application.Research;

/// <summary>Handed to a run query by a caller that will ask for trade cards afterwards; the run handler puts the <see cref="ExplainBasis"/> of the run in it (P3-04d).</summary>
public sealed class ExplainBasisSink
{
    /// <summary>Set by the run handler once the strategy is prepared and the calendar read; null before, or when the run was refused.</summary>
    public ExplainBasis? Basis { get; set; }
}
