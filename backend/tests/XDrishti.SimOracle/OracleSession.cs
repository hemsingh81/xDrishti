namespace XDrishti.SimOracle;

/// <summary>A session window in minutes of the day: it opens at <c>FromMinute</c> and ends at <c>ToMinute</c> (the due minute starts at <c>ToMinute - 1</c>).</summary>
public sealed record OracleSession(int FromMinute, int ToMinute)
{
    /// <summary>The regular session, 09:15 to 15:30.</summary>
    public static OracleSession Regular { get; } = new(555, 930);
}
