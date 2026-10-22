using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Compilation;

/// <summary>One parameter of an exit policy or overlay as the strategy file spells it: the name, whether it takes a list of candidate values, and whether it is required. A read-only view of <c>ExitTables</c>.</summary>
public sealed record ExitParameterInfo(string Name, bool IsList, bool Required)
{
    /// <summary>The most values in a list parameter.</summary>
    public const int MaxListValues = ExitTables.MaxListValues;

    /// <summary>The parameters of a policy.</summary>
    public static IReadOnlyList<ExitParameterInfo> Of(ExitPolicy policy) => From(ExitTables.Checked[policy]);

    /// <summary>The parameters of an overlay.</summary>
    public static IReadOnlyList<ExitParameterInfo> Of(OverlayKind kind) => From(ExitTables.OverlayChecked[kind]);

    private static ExitParameterInfo[] From(IReadOnlyList<ParameterSpec> specs) => [.. specs.Select(s => new ExitParameterInfo(s.Name, s.Shape == ParameterShape.List, s.Required))];
}
