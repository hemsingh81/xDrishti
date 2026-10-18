namespace XDrishti.Strategies.Model;

/// <summary>How long a strategy holds (doc 07 §3): an intraday strategy is flat by its square-off time, a swing strategy is held for sessions and ends at <c>max_hold_sessions</c>.</summary>
public enum Style
{
    /// <summary>Held within one session; needs a <c>square_off</c> time.</summary>
    Intraday,

    /// <summary>Held over sessions; needs <c>max_hold_sessions</c>.</summary>
    Swing,
}
