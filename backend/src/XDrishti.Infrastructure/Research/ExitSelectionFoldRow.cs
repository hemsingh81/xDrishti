namespace XDrishti.Infrastructure.Research;

/// <summary>A row of <c>research.exit_selection_folds</c> (spec P4-06, D10): one candidate in one fold of a stored exit choice. Written only by the adapter's SQL.</summary>
internal sealed class ExitSelectionFoldRow
{
    public long SelectionId { get; set; }

    public int FoldIndex { get; set; }

    public string CandidateId { get; set; } = string.Empty;

    public DateOnly TestStart { get; set; }

    public DateOnly TestEnd { get; set; }

    public bool Qualified { get; set; }

    public int TrainTrades { get; set; }

    public double? TrainScore { get; set; }

    public int TestTrades { get; set; }

    public double? TestScore { get; set; }

    public bool IsWinner { get; set; }
}
