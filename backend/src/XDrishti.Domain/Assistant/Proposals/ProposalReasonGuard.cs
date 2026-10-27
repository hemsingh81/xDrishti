using System.Globalization;
using XDrishti.Domain.Assistant.Text;

namespace XDrishti.Domain.Assistant.Proposals;

/// <summary>
/// Checks the model's reason against the engine's evidence (spec P9-08 AC 3, 5): the facts are the evidence figures and the proposal's own subject, never anything the model wrote. Every figure of the reason must be one of the evidence, every symbol must be one of the facts and a claim word
/// needs a figure of its kind; form rules are those of <see cref="TextGuard"/> (no markup, no address, no advice or forecast). Pure.
/// </summary>
public static class ProposalReasonGuard
{
    public const int MaxWords = 80;

    /// <summary>The codes of everything the reason says that the evidence does not back; empty when it passes.</summary>
    public static IReadOnlyList<string> Check(string reason, string subject, ProposalEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(reason);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(evidence);
        var lines = new List<FactLine> { new(string.Empty, "proposal", "proposal", Sentence(subject)) };
        foreach (var figure in evidence.Figures)
        {
            var text = figure.State == "value"
                ? string.Create(CultureInfo.InvariantCulture, $"{figure.Label}: {figure.Value}{(figure.N is { } n ? $" over {n} trades" : string.Empty)}.")
                : $"{figure.Label} is {figure.State.Replace('_', ' ')}.";
            lines.Add(new FactLine(string.Empty, "evidence", figure.Label, text, figure.State == "value" ? FactLine.Ok : FactLine.Unavailable));
        }

        lines.Add(new FactLine(string.Empty, "evidence", "sample", string.Create(CultureInfo.InvariantCulture, $"Sample size {evidence.N} trades."), FactLine.Ok));
        var facts = new BriefingFacts("proposal", subject, false, [], lines, null, null, []);
        var codes = new List<string>();
        foreach (var finding in TextGuard.Check(reason, facts, MaxWords).Findings)
        {
            codes.Add(finding.Code == GuardCodes.NumberNotInFacts ? ProposalCodes.ReasonNumberNotInEvidence : "reason_" + finding.Code);
        }

        if (UntrustedText.Clean(reason).InjectionFlags > 0)
        {
            codes.Add(ProposalCodes.ReasonInstructionLike);
        }

        if (ProposalWording.ClaimWordsIn(reason, facts.AllText()).Count > 0)
        {
            codes.Add(ProposalCodes.ReasonClaimWord);
        }

        return [.. codes.Distinct(StringComparer.Ordinal)];
    }

    private static string Sentence(string text) => text.EndsWith('.') ? text : text + ".";
}
