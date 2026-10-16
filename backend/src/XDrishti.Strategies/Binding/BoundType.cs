namespace XDrishti.Strategies.Binding;

/// <summary>
/// The type of a bound node (spec P3-02, <i>Types</i>). <see cref="Number"/> and <see cref="Bool"/> are the two types a finished condition is made of. <see cref="Time"/> (the keyword <c>time</c>), <see cref="Regime"/>
/// (the function <c>regime()</c>) and <see cref="Text"/> (a string literal) exist only so that the binder can check a comparison: a time is only ever compared with a <c>"HH:MM"</c> text and a regime with a
/// regime name or a code, and both comparisons become nodes of their own; a <see cref="Text"/> or <see cref="Time"/> node never survives in a tree without an error. <see cref="Invalid"/> is the type of a node that could
/// not be bound; the problem is already reported, and every rule treats it as "say nothing more about this", so one mistake does not bring a cascade of others.
/// </summary>
internal enum BoundType
{
    /// <summary>The node could not be bound; its problem is already reported.</summary>
    Invalid = 0,

    /// <summary>A double.</summary>
    Number,

    /// <summary>True, false or (at run time) unknown.</summary>
    Bool,

    /// <summary>The keyword <c>time</c>.</summary>
    Time,

    /// <summary>The market regime (1 to 9), read by <c>regime()</c>.</summary>
    Regime,

    /// <summary>A string literal.</summary>
    Text,
}
