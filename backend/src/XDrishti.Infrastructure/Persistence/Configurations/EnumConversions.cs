using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>Enums stored by their lower-case names (readable in the database, stable when the enum is reordered).</summary>
internal static class EnumConversions
{
    public static ValueConverter<T, string> Lowercase<T>()
        where T : struct, Enum =>
        new(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<T>(v, true));
}
