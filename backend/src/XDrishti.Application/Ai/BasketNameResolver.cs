using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Application.Ai;

/// <summary>What a basket name or id typed to the assistant resolved to: one basket, or none with up to five near names (untrusted text).</summary>
internal sealed record BasketMatch(Guid? Id, IReadOnlyList<string> NearNames);

/// <summary>
/// Resolves the text of a basket argument (an id or an exact name, case-insensitive) against the active baskets (spec BK-12 AC 6). An unknown or ambiguous name is no match; the near names (a name that contains the text or is contained in it) are returned so the model can ask. Pure.
/// </summary>
internal static class BasketNameResolver
{
    public const int MaxNear = 5;

    public static BasketMatch Resolve(string? text, IReadOnlyList<AiBasketRef> baskets)
    {
        ArgumentNullException.ThrowIfNull(baskets);
        var wanted = text?.Trim() ?? string.Empty;
        if (wanted.Length == 0)
        {
            return new BasketMatch(null, []);
        }

        if (Guid.TryParse(wanted, out var id) && baskets.Any(b => b.Id == id))
        {
            return new BasketMatch(id, []);
        }

        var exact = baskets.Where(b => string.Equals(b.Name, wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1)
        {
            return new BasketMatch(exact[0].Id, []);
        }

        var near = exact.Count > 1
            ? exact
            : [.. baskets.Where(b => b.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase) || wanted.Contains(b.Name, StringComparison.OrdinalIgnoreCase))];
        return new BasketMatch(null, [.. near.Select(b => b.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(MaxNear)]);
    }
}
