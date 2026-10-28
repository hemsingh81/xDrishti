using System.Globalization;

namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// The sentence stored beside a slippage row whose lower 95 % bound of the mean total slippage is above zero with n at least <c>Live:Estimates:ProposalMinN</c> (spec P11-02 criterion 10): the numbers and the knob it concerns. Nothing reads it automatically;
/// no estimate is applied to the simulator, the selection or a cell (decision 7).
/// </summary>
public static class SlippageProposal
{
    public const int MaxLength = 400;

    /// <summary>The sentence, or null when the row does not qualify.</summary>
    public static string? For(string bucket, string side, string style, string referenceKind, int n, double meanBps, double meanLoBps, int minN)
    {
        if (n < minN || !(meanLoBps > 0))
        {
            return null;
        }

        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"In {bucket} ({side}, {style}, against {referenceKind}) the mean total slippage is {meanBps:0.0} bp, 95 % lower bound {meanLoBps:0.0} bp, n = {n}: worse than the model assumes. The knob it concerns is slippage.ticks; nothing is applied automatically.");
        return text.Length <= MaxLength ? text : text[..MaxLength];
    }
}
