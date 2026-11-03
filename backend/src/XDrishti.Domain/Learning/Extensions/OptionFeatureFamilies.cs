namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The extension families of the options slices that the shared evaluation route accepts but the P15 phase check never iterates (P14-11): <c>ext:iv-1</c>. A list of its own, apart from <see cref="ExtensionFamilies.All"/>.</summary>
public static class OptionFeatureFamilies
{
    /// <summary>The option feature families.</summary>
    public static IReadOnlyList<string> All { get; } = [IvFeatureCatalog.Family];

    /// <summary>Whether the text names an option feature family.</summary>
    public static bool IsKnown(string? family) => family is not null && All.Contains(family, StringComparer.Ordinal);
}
