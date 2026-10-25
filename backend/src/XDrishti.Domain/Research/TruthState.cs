namespace XDrishti.Domain.Research;

/// <summary>The three values of a condition at a decision (doc 11 §2): unknown never fires.</summary>
public enum TruthState
{
    /// <summary>The condition was false.</summary>
    False,

    /// <summary>The condition was true.</summary>
    True,

    /// <summary>An operand was not ready; never fires an entry.</summary>
    Unknown,
}
