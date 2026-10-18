using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Compilation;

/// <summary>A condition list as compiled: its <paramref name="Path"/> (<c>buy.setup</c>), how its items combine and the items in the order of the model.</summary>
public sealed record CompiledList(string Path, ListMode Mode, IReadOnlyList<CompiledItem> Items);
