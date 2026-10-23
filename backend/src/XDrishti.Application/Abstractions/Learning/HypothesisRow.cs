namespace XDrishti.Application.Abstractions.Learning;

/// <summary>A row read back.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Row">The row.</param>
public sealed record HypothesisRow(long Id, NewHypothesisRow Row)
{
    /// <summary>The id of the hypothesis: the proposing row's id.</summary>
    public long HypothesisId => Row.HypothesisId ?? Id;
}
