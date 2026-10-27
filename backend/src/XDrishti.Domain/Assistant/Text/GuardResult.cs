namespace XDrishti.Domain.Assistant.Text;

/// <summary>The guard's verdict on one text: it passes when there is no finding.</summary>
public sealed record GuardResult(IReadOnlyList<GuardFinding> Findings, int WordCount)
{
    public bool Passed => Findings.Count == 0;
}
