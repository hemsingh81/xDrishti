namespace XDrishti.Domain.Filings.Extraction;

/// <summary>
/// The fenced user message of the announcement flag (P15-08): the subject, the category and the text sit between a begin and an end line that carry the same random token. The token is drawn again until it occurs nowhere in the content, so the text cannot contain the fence it is
/// held in: a fake closing fence in a text is a different token. Pure; the token source is a parameter.
/// </summary>
public static class AnnouncementFence
{
    private const int Attempts = 16;

    /// <summary>The message and its token; null only if no token that is absent from the content was found in <see cref="Attempts"/> draws (the caller then refuses the announcement).</summary>
    public static (string Message, string Token)? Wrap(string subject, string category, string text, Func<string> nextToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(nextToken);
        var body = $"subject: {subject}\ncategory: {category}\ntext:\n{text}";
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var token = nextToken();
            if (token.Length > 0 && !body.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return ($"<<<BEGIN {token}>>>\n{body}\n<<<END {token}>>>", token);
            }
        }

        return null;
    }
}
