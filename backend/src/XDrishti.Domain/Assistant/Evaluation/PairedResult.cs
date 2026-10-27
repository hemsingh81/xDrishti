namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>A paired comparison of two runs over the same cases (spec P9-09 AC 12): <paramref name="B"/> cases that passed before and fail now, <paramref name="C"/> the reverse, the exact two-sided sign-test <paramref name="PValue"/>.</summary>
public sealed record PairedResult(int Cases, int BothPass, int BothFail, int B, int C, double PValue)
{
    public int Discordant => B + C;
}
