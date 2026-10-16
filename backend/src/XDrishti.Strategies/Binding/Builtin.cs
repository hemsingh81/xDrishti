namespace XDrishti.Strategies.Binding;

/// <summary>The functions of the language that are not catalog features (spec P3-02, <i>Builtins</i>). The keyword <c>time</c> and the constants <c>MON … SUN</c> are not functions and are not here.</summary>
internal enum Builtin
{
    /// <summary><c>crosses_above(a, b)</c>.</summary>
    CrossesAbove = 0,

    /// <summary><c>crosses_below(a, b)</c>.</summary>
    CrossesBelow,

    /// <summary><c>rising(x, n)</c>.</summary>
    Rising,

    /// <summary><c>falling(x, n)</c>.</summary>
    Falling,

    /// <summary><c>between(x, low, high)</c>.</summary>
    Between,

    /// <summary><c>abs(x)</c>.</summary>
    Abs,

    /// <summary><c>min(a, b)</c>.</summary>
    Min,

    /// <summary><c>max(a, b)</c>.</summary>
    Max,

    /// <summary><c>regime()</c>: the catalog feature <c>regime</c>, typed as a regime so that it can be compared with the nine names.</summary>
    Regime,
}
