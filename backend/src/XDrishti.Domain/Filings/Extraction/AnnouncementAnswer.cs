namespace XDrishti.Domain.Filings.Extraction;

/// <summary>The model's answer about one announcement after the strict parse (P15-08): the flag, the category from the closed list, the short reason (text, shown escaped, never fed to a prompt) and the three optional fields. <see cref="OrderValueInr"/> is not yet checked against the text.</summary>
public sealed record AnnouncementAnswer(bool Material, string Category, string Reason, decimal? OrderValueInr, string Guidance, string RatingAction);

/// <summary>The result of the strict parse: the answer, or the closed reason it is not one. Exactly one of the two is set.</summary>
public sealed record AnswerParse(AnnouncementAnswer? Answer, string? Reason)
{
    public bool IsValid => Answer is not null;

    public static AnswerParse Ok(AnnouncementAnswer answer) => new(answer, null);

    public static AnswerParse Invalid(string reason) => new(null, reason);
}
