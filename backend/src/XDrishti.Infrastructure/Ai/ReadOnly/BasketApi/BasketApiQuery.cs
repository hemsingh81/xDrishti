namespace XDrishti.Infrastructure.Ai.ReadOnly.BasketApi;

/// <summary>The query string of a basket report call: the pairs with a value, each value escaped.</summary>
internal static class BasketApiQuery
{
    public static string Of(params (string Key, string? Value)[] pairs)
    {
        var parts = pairs.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}").ToList();
        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }
}
