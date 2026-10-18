namespace XDrishti.Domain.Research;

/// <summary>How the instruments of a run were chosen (spec P3-04a D2). Stored by name; never renamed.</summary>
public enum UniverseKind
{
    /// <summary>An explicit list of instrument ids, run in the order given.</summary>
    InstrumentList,

    /// <summary>The members of a manual basket as of today, in the basket's order.</summary>
    Basket,
}
