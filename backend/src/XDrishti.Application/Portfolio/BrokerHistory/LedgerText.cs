namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>Phrase matching on the free text of a ledger entry (Dhan's voucher description and narration): shared by the classifier (the class of an entry) and by the cost rule of the income figures.</summary>
internal static class LedgerText
{
    /// <summary>The narration and the description as one lower-case text (either may be missing).</summary>
    public static string Of(string? description, string? narration) =>
        ((narration ?? string.Empty) + " | " + (description ?? string.Empty)).ToLowerInvariant();

    /// <summary>
    /// Whether the text has the phrase at the start of a word (so "stt" is not found in "settlement"); a short phrase (four letters or fewer: "upi", "gst", "amc") must also end a word, a longer one may continue ("withdraw" finds "withdrawal").
    /// </summary>
    public static bool Has(string text, string phrase)
    {
        var from = 0;
        while (text.IndexOf(phrase, from, StringComparison.Ordinal) is var at and >= 0)
        {
            var end = at + phrase.Length;
            var startsWord = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
            var endsWord = phrase.Length > 4 || end == text.Length || !char.IsLetterOrDigit(text[end]);
            if (startsWord && endsWord)
            {
                return true;
            }

            from = at + 1;
        }

        return false;
    }
}
