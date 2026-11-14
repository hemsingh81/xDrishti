namespace XDrishti.SimReader;

/// <summary>Why the position left (protocol section 5: stop, target, square_off; max_hold for a swing hold, ADR 0040 X8; end_of_data is data, not a rule).</summary>
public enum ReaderExitReason
{
    Stop,
    Target,
    SquareOff,
    MaxHold,
    EndOfData,
}
