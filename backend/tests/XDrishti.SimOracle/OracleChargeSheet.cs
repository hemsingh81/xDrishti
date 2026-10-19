namespace XDrishti.SimOracle;

/// <summary>The charge sheet reduced to what the reference needs: an id and schedules ascending by date.</summary>
public sealed record OracleChargeSheet(string Id, IReadOnlyList<OracleSchedule> Schedules)
{
    /// <summary>The schedule in force on a date (the last one on or before it), or null before the first.</summary>
    public OracleSchedule? In(DateOnly date)
    {
        OracleSchedule? found = null;
        foreach (OracleSchedule s in Schedules)
        {
            if (s.EffectiveFrom <= date)
            {
                found = s;
            }
        }

        return found;
    }
}
