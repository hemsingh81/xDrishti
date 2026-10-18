namespace XDrishti.Strategies.Compilation;

/// <summary>One condition of a list, compiled alone: its <paramref name="Path"/> in the model (<c>buy.trigger[1]</c>) and the compiled condition, whose <see cref="CompiledCondition.Source"/> is the text as written.</summary>
public sealed record CompiledItem(string Path, CompiledCondition Condition);
