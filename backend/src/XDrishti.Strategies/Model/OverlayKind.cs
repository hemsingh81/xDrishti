namespace XDrishti.Strategies.Model;

/// <summary>Optional exits applied on top of every candidate policy (doc 16 <c>exits.overlays</c>, doc 08 §5), at most one of each kind.</summary>
public enum OverlayKind
{
    /// <summary>Leave a trade that has not progressed in time.</summary>
    TimeStop,

    /// <summary>Leave before an event (results).</summary>
    EventExit,
}
