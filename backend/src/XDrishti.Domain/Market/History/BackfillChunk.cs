namespace XDrishti.Domain.Market.History;

/// <summary>One request of the 1-minute backfill (CF-04 Part B): the India dates <c>[From, To)</c> of one instrument, half-open. A chunk never reaches the instrument's first CSV bar.</summary>
public readonly record struct BackfillChunk(DateOnly From, DateOnly To)
{
    public int Days => To.DayNumber - From.DayNumber;
}
