namespace XDrishti.Domain.Assistant.Text;

/// <summary>The finding codes of the guard (spec P9-06 AC 3 to 7, 11). They are stored and shown, so they never change.</summary>
public static class GuardCodes
{
    public const string NumberNotInFacts = "number_not_in_facts";
    public const string NumberWord = "number_word";
    public const string NumberWrongScope = "number_wrong_scope";
    public const string SymbolNotInFacts = "symbol_not_in_facts";
    public const string TicketNotCovered = "ticket_not_covered";
    public const string FormTooLong = "form_too_long";
    public const string FormMarkdown = "form_markdown";
    public const string FormUrl = "form_url";
    public const string FormCode = "form_code";
    public const string FormImperative = "form_imperative";
    public const string FormForecast = "form_forecast";
    public const string HonestyFigureForUnavailable = "honesty_figure_for_unavailable";
    public const string HonestyProvisionalMissing = "honesty_provisional_missing";
    public const string HonestyNeverSay = "honesty_never_say";
    public const string EvidenceDropped = "evidence_dropped";
    public const string Empty = "empty_text";

    /// <summary>The plain-words reason of each code, for the card.</summary>
    public static string Words(string code) => code switch
    {
        NumberNotInFacts => "a number that is not in the stored facts",
        NumberWord => "a number written as a word that the facts do not use",
        NumberWrongScope => "a number that belongs to another ticket",
        SymbolNotInFacts => "a symbol or name that is not in the stored facts",
        TicketNotCovered => "a planned ticket that was left out",
        FormTooLong => "longer than the word limit",
        FormMarkdown => "markup other than plain bullets",
        FormUrl => "a web address",
        FormCode => "a code block",
        FormImperative => "an instruction to trade",
        FormForecast => "a forecast or a promise",
        HonestyFigureForUnavailable => "a figure for something the facts mark as unavailable",
        HonestyProvisionalMissing => "it did not say the plan is provisional",
        HonestyNeverSay => "a forbidden claim",
        EvidenceDropped => "an insight's sample size was dropped",
        Empty => "no text",
        _ => code,
    };
}
