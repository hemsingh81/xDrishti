namespace XDrishti.SimReader;

/// <summary>The log spelling of an exit reason.</summary>
public static class ReaderExitReasonText
{
    public static string Code(this ReaderExitReason reason) => reason switch
    {
        ReaderExitReason.Stop => "stop",
        ReaderExitReason.Target => "target",
        ReaderExitReason.SquareOff => "square_off",
        ReaderExitReason.EndOfData => "end_of_data",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };
}
