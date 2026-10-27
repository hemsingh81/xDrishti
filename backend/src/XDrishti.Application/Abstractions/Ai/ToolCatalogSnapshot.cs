namespace XDrishti.Application.Abstractions.Ai;

/// <summary>The tools on offer now. <paramref name="Tools"/> is empty unless <paramref name="State"/> is <see cref="ToolCatalogState.Ready"/>.</summary>
public sealed record ToolCatalogSnapshot(ToolCatalogState State, IReadOnlyList<ToolDescriptor> Tools)
{
    public static ToolCatalogSnapshot Unavailable(ToolCatalogState state) => new(state, []);
}
