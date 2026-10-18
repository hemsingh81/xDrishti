namespace XDrishti.Strategies.Model;

/// <summary>
/// The candidate exit policies the Lab compares on the same entries (doc 08 §2 and §3). <c>SESSION_EXIT</c> and <c>MAX_HOLD</c> are always on and come from the time rules; <c>TIME_STOP</c> and <c>EVENT_EXIT</c> are
/// overlays (<see cref="OverlayKind"/>). Every policy but <see cref="FixedRr"/> is accepted by the model and refused by name by the simulator until P4 runs it.
/// </summary>
public enum ExitPolicy
{
    /// <summary>A target at a multiple of the risk (<c>rr</c>, a list of candidate multiples).</summary>
    FixedRr,

    /// <summary>Book part at a multiple of the risk and move the stop to break-even.</summary>
    PartialBe,

    /// <summary>Book part at a multiple of the risk and trail the rest with the ATR.</summary>
    PartialTrail,

    /// <summary>Trail the stop at a multiple of the ATR.</summary>
    AtrTrail,

    /// <summary>Trail the stop under the structure (swing lows or highs).</summary>
    StructureTrail,

    /// <summary>Trail the stop under a moving average.</summary>
    EmaTrail,

    /// <summary>Add to a winner in steps.</summary>
    Pyramid,
}
