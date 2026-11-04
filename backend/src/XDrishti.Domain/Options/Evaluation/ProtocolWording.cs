namespace XDrishti.Domain.Options.Evaluation;

/// <summary>
/// The words of a protocol that shares the core of protocol O (P14-10): the protocol letter of the evidence, the name of the unit it counts, the name of the missing quantity when there is none, the label of the mean it prints, and whether the study has a cash twin and so a futures baseline.
/// Protocol O (a structure or a long option against its cash trade) and protocol R (a condor on its own out-of-sample R, with no cash twin) differ in these words only; every threshold is in <see cref="ExpressionStudyOptions"/>.
/// </summary>
public sealed record ProtocolWording(string Letter, string Pairs, string NoPairs, string Mean, bool CashTwin)
{
    /// <summary>Protocol O: the paired difference against the cash trade.</summary>
    public static ProtocolWording Expression { get; } = new("O", "paired trades", "no_paired_trades", "mean d (option - cash, R)", true);

    /// <summary>Protocol R: the condor's own R, nothing to pair with.</summary>
    public static ProtocolWording Condor { get; } = new("R", "condors", "no_condors", "mean R (net rupees / maximum loss)", false);
}
