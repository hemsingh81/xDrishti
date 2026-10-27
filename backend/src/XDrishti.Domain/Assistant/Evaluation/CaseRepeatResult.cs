namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// One graded repeat of one case, as the summary reads it. <paramref name="Flags"/> hold the set's own booleans: data <c>hard_fail</c>; plans <c>raw</c>, <c>repaired</c>, <c>escape</c>, <c>covered</c>; ideas <c>first_shot</c>, <c>valid</c>, <c>decline_applicable</c>, <c>decline_correct</c>.
/// <paramref name="Score"/> is the intent of an idea. <paramref name="AnswerKey"/> is the normalised answer, for the stability of the repeats.
/// </summary>
public sealed record CaseRepeatResult(string Set, string CaseId, int Repeat, bool Passed, bool? Grounded, double? Score, IReadOnlyDictionary<string, bool> Flags, string AnswerKey)
{
    public bool Flag(string name) => Flags.TryGetValue(name, out var value) && value;
}
