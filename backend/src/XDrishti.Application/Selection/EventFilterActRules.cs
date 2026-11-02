using System.Text.RegularExpressions;

namespace XDrishti.Application.Selection;

/// <summary>The checks of an owner act on the event-risk switch (P15-06): the actor is <c>owner:&lt;e-mail&gt;</c>, the reason is 1 to 300 characters and names no secret. The same words as the other owner acts; kept here so that the selection area names no execution type.</summary>
internal static partial class EventFilterActRules
{
    private const string OwnerPrefix = "owner:";

    [GeneratedRegex("(token|secret|password|apikey|authorization)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Forbidden();

    public static bool IsOwner(string actor) => actor.StartsWith(OwnerPrefix, StringComparison.Ordinal) && actor.Length > OwnerPrefix.Length;

    public static bool ReasonOk(string? reason) => reason?.Trim() is { Length: > 0 and <= 300 } text && !Forbidden().IsMatch(text);
}
