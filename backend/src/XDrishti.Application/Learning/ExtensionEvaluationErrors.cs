using XDrishti.Domain.Common;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Learning;

/// <summary>Stable problem codes of the extension evaluations (P15-10).</summary>
public static class ExtensionEvaluationErrors
{
    public const string InvalidQuery = "extensions.invalid_query";

    public const string NotFound = "extensions.not_found";

    public const string HoldOutUsed = "extensions.holdout_used";

    public static Error InvalidFamily(string? family) => Error.Validation(InvalidQuery, "The family is one of " + string.Join(", ", ExtensionFamilies.All.Concat(PairFamilies.All)) + (family is null ? "." : ", not '" + Truncate(family) + "'."));

    public static Error Missing(string id) => Error.NotFound(NotFound, "No extension evaluation with id '" + Truncate(id) + "' is stored.");

    private static string Truncate(string text) => text.Length <= 80 ? text : text[..80];
}
