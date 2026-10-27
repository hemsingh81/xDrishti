using System.Text.RegularExpressions;
using XDrishti.Domain.Assistant.Grounding;

namespace XDrishti.Domain.Assistant.Text;

/// <summary>
/// The checker that rejects (spec P9-06 AC 3 to 7, 11, D1, D2, D8): reads a text written by a model against the facts it was given and lists everything it cannot verify. Every number must be a number of the facts (rounding allowed, arithmetic not), in the sentence of the
/// ticket it belongs to; every symbol must be a symbol of the facts; every planned ticket must be named; a fact marked unavailable keeps its word; and the text takes no form of advice, forecast or markup. It checks where a figure comes from; it cannot check that a sentence is true or well judged
/// (see the risks of the spec). Pure; the facts are the only ground (never the model's own text or arguments).
/// </summary>
public static partial class TextGuard
{
    private static readonly Regex NumberWordPattern = new(@"\b(?:" + string.Join('|', GuardRules.NumberWords) + @")\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>The number of white-space separated words of a text (what the word limit counts).</summary>
    public static int WordCount(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Word().Count(text);
    }

    public static GuardResult Check(string text, BriefingFacts facts, int maxWords)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(facts);
        var findings = new List<GuardFinding>();
        var words = Word().Count(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new GuardResult([new GuardFinding(GuardCodes.Empty, string.Empty, 0)], 0);
        }

        Form(text, words, maxWords, findings);
        var all = facts.AllText();
        Numbers(text, facts, all, findings);
        Symbols(text, facts, all, findings);
        Honesty(text, facts, findings);
        return new GuardResult([.. findings.OrderBy(f => f.Offset).ThenBy(f => f.Code, StringComparer.Ordinal)], words);
    }

    private static void Form(string text, int words, int maxWords, List<GuardFinding> findings)
    {
        if (words > maxWords)
        {
            findings.Add(new GuardFinding(GuardCodes.FormTooLong, string.Empty, 0));
        }

        AddAll(Code(), text, GuardCodes.FormCode, findings);
        AddAll(Markdown(), text, GuardCodes.FormMarkdown, findings);
        AddAll(Url(), text, GuardCodes.FormUrl, findings);
        foreach (var pattern in GuardRules.ForecastPatterns)
        {
            foreach (Match match in Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                findings.Add(new GuardFinding(GuardCodes.FormForecast, match.Value, match.Index));
            }
        }

        foreach (var (start, end) in Sentences(text))
        {
            var sentence = text.AsSpan(start, end - start);
            var lead = 0;
            while (lead < sentence.Length && (char.IsWhiteSpace(sentence[lead]) || sentence[lead] == '-'))
            {
                lead++;
            }

            var stop = lead;
            while (stop < sentence.Length && char.IsLetter(sentence[stop]))
            {
                stop++;
            }

            var first = sentence[lead..stop].ToString();
            if (first.Length > 0 && GuardRules.ImperativeVerbs.Contains(first, StringComparer.OrdinalIgnoreCase))
            {
                findings.Add(new GuardFinding(GuardCodes.FormImperative, first, start + lead));
            }
        }
    }

    private static void Numbers(string text, BriefingFacts facts, string all, List<GuardFinding> findings)
    {
        var numbers = FactNumber.Of(facts);
        var sentences = Sentences(text);
        var named = sentences.Select(s => Named(text.AsSpan(s.Start, s.End - s.Start).ToString(), facts)).ToList();
        foreach (var figure in NumberTokenizer.Figures(text))
        {
            var at = IndexOf(sentences, figure.Start);
            var tickets = at >= 0 ? named[at] : [];
            var negative = FactNumber.IsNegative(figure.Text);
            if (figure.Number is not { } parsed || !numbers.Any(n => n.Matches(parsed, negative)))
            {
                findings.Add(new GuardFinding(GuardCodes.NumberNotInFacts, figure.Text.Trim(), figure.Start));
            }
            else if (!numbers.Any(n => (n.Scope.Length == 0 || tickets.Contains(n.Scope, StringComparer.Ordinal)) && n.Matches(parsed, negative)))
            {
                findings.Add(new GuardFinding(GuardCodes.NumberWrongScope, figure.Text.Trim(), figure.Start));
            }
        }

        // Digits the shared tokenizer does not call figures: glued to letters (pnl1250, net-45) or a year-like integer (1950). They must occur verbatim in the facts.
        var figures = NumberTokenizer.Figures(text);
        foreach (Match word in DigitWord().Matches(text))
        {
            var token = word.Value.Trim('.', ',', ';', ':', '(', ')', '"', '\'');
            if (!figures.Any(f => f.Start < word.Index + word.Length && f.End > word.Index) && !all.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new GuardFinding(GuardCodes.NumberNotInFacts, token, word.Index));
            }
        }

        foreach (Match year in YearLike().Matches(text))
        {
            if (!all.Contains(year.Value, StringComparison.Ordinal))
            {
                findings.Add(new GuardFinding(GuardCodes.NumberNotInFacts, year.Value, year.Index));
            }
        }

        foreach (var pattern in new[] { IsoDate(), Clock(), WrittenDate() })
        {
            foreach (Match match in pattern.Matches(text))
            {
                if (!all.Contains(match.Value, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new GuardFinding(GuardCodes.NumberNotInFacts, match.Value, match.Index));
                }
            }
        }

        foreach (Match match in NumberWord().Matches(text))
        {
            if (!Regex.IsMatch(all, @"\b" + Regex.Escape(match.Value) + @"\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                findings.Add(new GuardFinding(GuardCodes.NumberWord, match.Value, match.Index));
            }
        }

        foreach (var insight in facts.Lines.Where(l => l.Section == "insight"))
        {
            var count = InsightSample().Match(insight.Text);
            if (count.Success && !text.Contains(count.Groups[1].Value, StringComparison.Ordinal))
            {
                findings.Add(new GuardFinding(GuardCodes.EvidenceDropped, count.Groups[1].Value, 0));
            }
        }
    }

    private static void Symbols(string text, BriefingFacts facts, string all, List<GuardFinding> findings)
    {
        var allowed = new HashSet<string>(BriefingWording.AllowedAcronyms, StringComparer.Ordinal);
        allowed.UnionWith(facts.Tickets);
        foreach (var line in facts.Lines)
        {
            allowed.Add(line.Scope);
        }

        foreach (Match match in Symbol().Matches(all))
        {
            allowed.Add(Trim(match.Value));
        }

        foreach (Match match in Symbol().Matches(text))
        {
            var token = Trim(match.Value);
            if (token.Length >= 3 && !allowed.Contains(token))
            {
                findings.Add(new GuardFinding(GuardCodes.SymbolNotInFacts, token, match.Index));
            }
        }

        if (facts.Kind == BriefingFacts.Daily)
        {
            var present = new HashSet<string>(Symbol().Matches(text).Select(m => Trim(m.Value)), StringComparer.Ordinal);
            foreach (var ticket in facts.Tickets.Where(t => !present.Contains(t)))
            {
                findings.Add(new GuardFinding(GuardCodes.TicketNotCovered, ticket, 0));
            }
        }
    }

    private static void Honesty(string text, BriefingFacts facts, List<GuardFinding> findings)
    {
        var figures = NumberTokenizer.Figures(text);
        foreach (var (start, end) in Sentences(text))
        {
            foreach (var line in facts.Lines.Where(l => l.NoFigures && l.Label.Length > 0))
            {
                if (text.AsSpan(start, end - start).Contains(line.Label, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var figure in figures.Where(f => f.Start >= start && f.Start < end))
                    {
                        findings.Add(new GuardFinding(GuardCodes.HonestyFigureForUnavailable, figure.Text.Trim(), figure.Start));
                    }
                }
            }
        }

        if (facts.Provisional && !text.Contains("provisional", StringComparison.OrdinalIgnoreCase) && !text.Contains("not paper-proven", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new GuardFinding(GuardCodes.HonestyProvisionalMissing, string.Empty, 0));
        }

        foreach (var phrase in BriefingWording.NeverSay)
        {
            foreach (Match match in Regex.Matches(text, @"\b" + Regex.Escape(phrase) + @"\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                findings.Add(new GuardFinding(GuardCodes.HonestyNeverSay, match.Value, match.Index));
            }
        }
    }

    private static List<string> Named(string sentence, BriefingFacts facts)
    {
        var present = new HashSet<string>(Symbol().Matches(sentence).Select(m => Trim(m.Value)), StringComparer.Ordinal);
        return [.. facts.Tickets.Where(present.Contains)];
    }

    private static string Trim(string token) => token.TrimEnd('-', '&');

    private static int IndexOf(List<(int Start, int End)> sentences, int position)
    {
        for (var i = 0; i < sentences.Count; i++)
        {
            if (position >= sentences[i].Start && position < sentences[i].End)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The sentences of a text: a line break or a full stop, question or exclamation mark followed by white space ends one (a decimal point does not).</summary>
    private static List<(int Start, int End)> Sentences(string text)
    {
        var list = new List<(int, int)>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var ends = text[i] == '\n' || (text[i] is '.' or '!' or '?' && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1])));
            if (ends)
            {
                list.Add((start, i + 1));
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            list.Add((start, text.Length));
        }

        return list;
    }

    private static void AddAll(Regex pattern, string text, string code, List<GuardFinding> findings)
    {
        foreach (Match match in pattern.Matches(text))
        {
            findings.Add(new GuardFinding(code, match.Value.Trim(), match.Index));
        }
    }

    [GeneratedRegex(@"\S+", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Word();

    [GeneratedRegex(@"\S*\d\S*", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex DigitWord();

    [GeneratedRegex(@"(?<![\d.,])(?:19|20)\d{2}(?![\d,]|\.\d)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex YearLike();

    [GeneratedRegex(@"`", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Code();

    [GeneratedRegex(@"(?m)^[ \t]*(?:#{1,6}[ \t]|>|\*[ \t]|\d+[.)][ \t]|\|)|\*\*|__", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Markdown();

    [GeneratedRegex(@"https?://\S+|www\.\S+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex Url();

    [GeneratedRegex(@"(?<![A-Za-z0-9&_-])[A-Z][A-Z0-9&-]{2,}(?![A-Za-z0-9&_])", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Symbol();

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}\b", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"\b\d{1,2}:\d{2}\b", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Clock();

    [GeneratedRegex(@"\b\d{1,2}(?:st|nd|rd|th)?\s+(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\b|\b(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?\s+\d{1,2}(?:st|nd|rd|th)?\b", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex WrittenDate();

    private static Regex NumberWord() => NumberWordPattern;

    [GeneratedRegex(@"over\s+(\d[\d,]*)\s+trades", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex InsightSample();
}
