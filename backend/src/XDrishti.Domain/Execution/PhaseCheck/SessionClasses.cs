namespace XDrishti.Domain.Execution.PhaseCheck;

/// <summary>The words of the session classes, as printed and as sent in the object.</summary>
public static class SessionClasses
{
    public static string Word(SessionClass value) => value switch
    {
        SessionClass.OutOfMode => "out_of_mode",
        SessionClass.Broken => "broken",
        SessionClass.Idle => "idle",
        SessionClass.Counted => "counted",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
