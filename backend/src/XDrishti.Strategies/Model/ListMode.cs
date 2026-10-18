namespace XDrishti.Strategies.Model;

/// <summary>How the items of a <see cref="ConditionList"/> combine: all of them must hold (Kleene AND), or any one of them (Kleene OR).</summary>
public enum ListMode
{
    /// <summary>Every item must be true.</summary>
    All,

    /// <summary>At least one item must be true.</summary>
    Any,
}
