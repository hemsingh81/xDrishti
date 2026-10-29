namespace XDrishti.Execution.Domain;

public enum HaltReading
{
    Clear,
    Halted,
    Unreadable,
}

public static class HaltPolicy
{
    /// <summary>Only a readable, clear store lets an entry leave: <see cref="HaltReading.Unreadable"/> means halted.</summary>
    public static bool EntriesBlocked(HaltReading reading) => reading != HaltReading.Clear;
}
