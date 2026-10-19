using System.Collections.Frozen;
using XDrishti.Application.Abstractions.Research;

namespace XDrishti.Infrastructure.Research;

/// <summary>Enums stored by their lower-case names (the pattern of <c>EnumConversions.Lowercase</c>), read back strictly: a name the enum does not have, and a number, are refused (<c>store.row_unreadable</c>), never turned into a wrong value.</summary>
internal static class EnumText<T>
    where T : struct, Enum
{
    private static readonly FrozenDictionary<string, T> ByName = Enum.GetValues<T>().ToFrozenDictionary(v => Name(v), v => v, StringComparer.Ordinal);

    /// <summary>The lower-case names of the enum, in declaration order.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Enum.GetValues<T>().Select(Name)];

    public static string Name(T value) => value.ToString().ToLowerInvariant();

    public static T Parse(string text) =>
        ByName.TryGetValue(text, out var value) ? value : throw new StoreReadException($"A stored {typeof(T).Name} has the unknown name '{(text.Length > 24 ? text[..24] : text)}'.");
}
