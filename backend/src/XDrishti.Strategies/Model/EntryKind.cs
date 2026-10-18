namespace XDrishti.Strategies.Model;

/// <summary>How an entry is placed once the sections allow it (doc 11 §2). The kinds carry no fields in schema 1; the simulator runs <see cref="AtClose"/> first and refuses the others by name until it is built for them.</summary>
public enum EntryKind
{
    /// <summary>At the close of the trigger candle.</summary>
    AtClose,

    /// <summary>A stop order above the high of the trigger candle.</summary>
    StopAboveHigh,

    /// <summary>A limit order below the trigger candle (a pullback).</summary>
    LimitPullback,
}
