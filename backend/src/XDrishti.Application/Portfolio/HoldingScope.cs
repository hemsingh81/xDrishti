using XDrishti.Domain.Common;

namespace XDrishti.Application.Portfolio;

/// <summary>The scope filters the holdings list and the holding groups share: <c>q</c> (symbol, name, ISIN) and <c>status</c> (open by default).</summary>
internal static class HoldingScope
{
    public const int MaxTextLength = 100;

    /// <summary>Checks <c>q</c> and <c>status</c>; returns the trimmed text (null: none) and the status to read ("open", "closed" or null for all). Problems are added to <paramref name="errors"/>.</summary>
    public static (string? Text, string? Status) Parse(string? q, string? status, FieldErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var text = q?.Trim();
        if (text is { Length: > MaxTextLength })
        {
            errors.Add("q", $"Use at most {MaxTextLength} characters.");
        }
        else if (text is not null && text.Any(char.IsControl))
        {
            errors.Add("q", "Control characters are not allowed.");
        }

        string? wanted = "open";
        if (!string.IsNullOrWhiteSpace(status))
        {
            switch (status.Trim().ToLowerInvariant())
            {
                case "open":
                    break;
                case "closed":
                    wanted = "closed";
                    break;
                case "all":
                    wanted = null;
                    break;
                default:
                    errors.Add("status", "Use open, closed or all.");
                    break;
            }
        }

        return (string.IsNullOrEmpty(text) ? null : text, wanted);
    }
}
