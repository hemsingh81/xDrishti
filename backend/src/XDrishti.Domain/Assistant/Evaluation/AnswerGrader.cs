using System.Text.RegularExpressions;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// Grades a final answer by code (spec P9-09 D2): exact normalised numbers (AC 3), set membership for words, state phrases for honest cases, a conflict check in the sentences that carry the answer (AC 2) and a grounding check of every figure
/// against the tool results (AC 4). No model judge, no tolerance band. Pure and total.
/// </summary>
public static partial class AnswerGrader
{
    /// <summary>Grades <paramref name="answer"/>. <paramref name="toolResults"/> are the stored tool result texts of the conversation; null means the audit is missing and grounding is not evaluable (the case cannot pass).</summary>
    public static AnswerGrade Grade(EvalCase evalCase, string answer, IReadOnlyList<string>? toolResults)
    {
        ArgumentNullException.ThrowIfNull(evalCase);
        ArgumentNullException.ThrowIfNull(answer);
        var codes = new List<string>();
        var figures = AnswerNumbers.Find(answer);
        var known = KnownNumbers(evalCase);
        if (string.IsNullOrWhiteSpace(answer))
        {
            codes.Add(EvalCodes.EmptyAnswer);
        }
        else
        {
            foreach (var part in evalCase.Parts)
            {
                CheckPart(part, answer, figures, known, codes);
            }

            if (evalCase.Parts.Any(p => p is NumberPart or IntegerPart))
            {
                CheckConflicts(evalCase, answer, figures, known, codes);
            }
        }

        var correct = codes.Count == 0;
        var ungrounded = new List<string>();
        bool? grounded = null;
        if (toolResults is not null)
        {
            var sources = SourceNumbers(toolResults);
            ungrounded = [.. figures.Where(f => !known.Accepts(f) && !sources.Any(s => s.Supports(f))).Select(f => f.Text.Trim())];
            grounded = ungrounded.Count == 0;
            if (ungrounded.Count > 0)
            {
                codes.Add(EvalCodes.UngroundedNumber);
            }
        }
        else
        {
            codes.Add(EvalCodes.GroundingNotEvaluable);
        }

        return new AnswerGrade(evalCase.Id, correct, grounded, evalCase.IsHonest && ungrounded.Count > 0, new ValueList<string>(codes), new ValueList<string>(ungrounded));
    }

    /// <summary>True when the printed figure is an accepted spelling of the expected number: at least the minimum decimals, equal to the exact value rounded half away from zero to the printed decimals.</summary>
    public static bool IsAccepted(NumberPart part, AnswerNumber figure)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(figure);
        return figure.Decimals >= part.MinDecimals && SameAsRounded(part.Value, figure);
    }

    private static bool SameAsRounded(EvalFraction value, AnswerNumber figure) => figure.Decimals <= 12 && value.Round(figure.Decimals) == figure.Value;

    private static void CheckPart(ExpectedPart part, string answer, IReadOnlyList<AnswerNumber> figures, Known known, List<string> codes)
    {
        switch (part)
        {
            case NumberPart number:
                if (figures.Any(f => IsAccepted(number, f)))
                {
                    return;
                }

                codes.Add(figures.Count == 0 ? EvalCodes.MissingNumber : figures.Any(f => f.Decimals < number.MinDecimals && SameAsRounded(number.Value, f)) ? EvalCodes.TooFewDecimals : EvalCodes.WrongNumber);
                break;
            case IntegerPart integer:
                if (!figures.Any(f => IsInteger(integer, f)))
                {
                    codes.Add(figures.Count == 0 ? EvalCodes.MissingNumber : EvalCodes.WrongNumber);
                }

                break;
            case TextPart text:
                if (!text.ContainsAny.Any(w => HasWord(answer, w)))
                {
                    codes.Add(EvalCodes.MissingText);
                }

                if (text.Forbid.Any(w => HasWord(answer, w)))
                {
                    codes.Add(EvalCodes.ForbiddenText);
                }

                break;
            case HonestPart honest:
                if (!honest.Phrases.Any(p => answer.Contains(p, StringComparison.OrdinalIgnoreCase)))
                {
                    codes.Add(EvalCodes.HonestStateMissing);
                }

                if (honest.Withheld is { Length: > 0 } withheld && WithheldFigures(answer, withheld, figures).Any(f => !known.Accepts(f)))
                {
                    codes.Add(EvalCodes.WithheldNumberGiven);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(part));
        }
    }

    private static bool IsInteger(IntegerPart part, AnswerNumber f) => f.Decimals == 0 && !f.IsPercent && f.Value == part.Value;

    private static IEnumerable<AnswerNumber> WithheldFigures(string answer, string withheld, IReadOnlyList<AnswerNumber> figures)
    {
        var spans = AnswerNumbers.Sentences(answer).Where(s => answer.AsSpan(s.Start, s.End - s.Start).Contains(withheld, StringComparison.OrdinalIgnoreCase)).ToList();
        return figures.Where(f => spans.Any(s => f.Start >= s.Start && f.End <= s.End));
    }

    private static void CheckConflicts(EvalCase evalCase, string answer, IReadOnlyList<AnswerNumber> figures, Known known, List<string> codes)
    {
        var sentences = AnswerNumbers.Sentences(answer).Where(s => evalCase.Anchors.Count == 0 || evalCase.Anchors.Any(a => answer.AsSpan(s.Start, s.End - s.Start).Contains(a, StringComparison.OrdinalIgnoreCase))).ToList();
        var parts = evalCase.Parts;
        bool Expected(AnswerNumber f) => parts.Any(p => p switch { NumberPart n => IsAccepted(n, f), IntegerPart i => IsInteger(i, f), _ => false });
        if (figures.Any(f => sentences.Any(s => f.Start >= s.Start && f.End <= s.End) && !Expected(f) && !known.Accepts(f)))
        {
            codes.Add(EvalCodes.ConflictingNumber);
        }
    }

    private static bool HasWord(string text, string word) =>
        Regex.IsMatch(text, @"(?<![\p{L}\d])" + Regex.Escape(word) + @"(?![\p{L}\d])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static Known KnownNumbers(EvalCase evalCase) =>
        new([.. AnswerNumbers.Find(evalCase.Question).Select(n => n.Value)], evalCase.AllowedNumbers);

    private static List<Source> SourceNumbers(IReadOnlyList<string> results)
    {
        var found = new List<Source>();
        foreach (var text in results)
        {
            foreach (Match m in SourceFigure().Matches(text))
            {
                var clean = m.Value.Replace(",", string.Empty, StringComparison.Ordinal);
                if (decimal.TryParse(clean, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var value))
                {
                    var point = clean.IndexOf('.', StringComparison.Ordinal);
                    found.Add(new Source(value, point < 0 ? 0 : Math.Min(clean.Length - point - 1, 20)));
                }
            }
        }

        return found;
    }

    // Every digit run of a tool result, with its decimals; a grouped number is read both whole and in parts (a JSON list [1,234] has two).
    [GeneratedRegex(@"\d{1,3}(?:,\d{2,3})+(?:\.\d+)?|\d+(?:\.\d+)?", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex SourceFigure();

    private sealed record Source(decimal Value, int Decimals)
    {
        public bool Supports(AnswerNumber f) => Matches(Value, Decimals, f) || (f.IsPercent && Value <= decimal.MaxValue / 100 && Matches(Value * 100, Math.Max(0, Decimals - 2), f));

        private static bool Matches(decimal value, int decimals, AnswerNumber f)
        {
            var magnitude = Math.Abs(f.Value);
            return f.Decimals >= decimals ? magnitude == value : Math.Round(value, f.Decimals, MidpointRounding.AwayFromZero) == magnitude;
        }
    }

    private sealed record Known(IReadOnlyList<decimal> QuestionValues, IReadOnlyList<EvalFraction> Allowed)
    {
        /// <summary>The figure is in the question (exactly) or is a spelling of an allowed number (sign ignored; rounded forms need two decimals when the allowed number has more).</summary>
        public bool Accepts(AnswerNumber f)
        {
            if (QuestionValues.Any(q => Math.Abs(q) == Math.Abs(f.Value)))
            {
                return true;
            }

            foreach (var allowed in Allowed)
            {
                var needed = 0;
                while (needed < 12 && !allowed.EqualsDecimal(allowed.Round(needed)))
                {
                    needed++;
                }

                if (f.Decimals >= Math.Min(needed, 2) && f.Decimals <= 12 && Math.Abs(allowed.Round(f.Decimals)) == Math.Abs(f.Value))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
