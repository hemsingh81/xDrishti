using System.Text.RegularExpressions;

namespace XDrishti.Domain.Assistant.Proposals;

/// <summary>The pinned words of the proposals (spec P9-08 AC 5): code decides what a reason may claim, the model does not.</summary>
public static class ProposalWording
{
    /// <summary>Words that claim a kind of strength; a reason may use one only when an evidence figure of that kind is present (none of the figures built in this slice is).</summary>
    public static IReadOnlyList<string> ClaimWords { get; } = ["strong", "best", "outperform", "momentum", "breakout"];

    /// <summary>The claim words a reason uses (a word and its inflections: "strongest", "outperforms", "outperformed"), unless the evidence text itself carries that word.</summary>
    public static IReadOnlyList<string> ClaimWordsIn(string reason, string evidenceText)
    {
        ArgumentNullException.ThrowIfNull(reason);
        ArgumentNullException.ThrowIfNull(evidenceText);
        var found = new List<string>();
        foreach (var word in ClaimWords)
        {
            var pattern = new Regex(@"\b" + word + @"[a-z]*\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (pattern.IsMatch(reason) && !pattern.IsMatch(evidenceText))
            {
                found.Add(word);
            }
        }

        return found;
    }

    /// <summary>Each decided proposal says what accepting did and did not do.</summary>
    public const string AcceptNote = "Accepting changes a basket or writes a proposed hypothesis. It never tracks an instrument, starts a backfill, approves or runs anything, or places an order.";

    /// <summary>Printed with every hypothesis proposal.</summary>
    public const string TrialNote = "Each judged hypothesis counts as a trial and raises the bar for the next.";

    /// <summary>The longest reason (the column is 400).</summary>
    public const int MaxReason = 400;
}
