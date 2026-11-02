namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The extension input of <see cref="TrainingDataBuilder"/> (P13-05): the group, the stored values by trade key (<see cref="ExtensionGroup.Names"/> order, NaN = unknown) and the names a row must have known.</summary>
/// <param name="Group">The extension group.</param>
/// <param name="Values">The values by trade key.</param>
/// <param name="Required">Extension names whose unknown value drops the trade; null or empty means none (an unknown value stays NaN).</param>
public sealed record ExtensionGroupInput(ExtensionGroup Group, IReadOnlyDictionary<string, double[]> Values, IReadOnlyList<string>? Required = null)
{
    /// <summary>The problem with the input, or null when it is usable.</summary>
    public string? Problem() =>
        Group is null ? "no extension group"
        : Values is null ? "no extension values"
        : Group.Problem() ?? (Required?.FirstOrDefault(n => !Group.Names.Contains(n, StringComparer.Ordinal)) is { } unknown ? "the required extension name '" + unknown + "' is not in the group" : null);
}
