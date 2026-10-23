namespace XDrishti.Domain.Planning;

/// <summary>One suggestion for the owner's inbox (spec P7-09). A suggestion changes nothing by itself; it is text the owner reads in the review.</summary>
/// <param name="Kind">A <see cref="SuggestionKinds"/> value.</param>
/// <param name="Subject">What it is about; with the plan and the kind it identifies the suggestion (the same triple is stored once).</param>
/// <param name="Text">In words, at most <see cref="MaxTextLength"/> characters.</param>
public sealed record NewSuggestion(string Kind, string Subject, string Text)
{
    /// <summary>The longest text stored.</summary>
    public const int MaxTextLength = 500;
}
