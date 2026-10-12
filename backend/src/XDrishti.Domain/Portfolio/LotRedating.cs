namespace XDrishti.Domain.Portfolio;

/// <summary>What re-dating the default-dated lots did: lots moved to the new date, and lots left alone because they were closed before it.</summary>
public readonly record struct LotRedating(int Redated, int Skipped)
{
    public static LotRedating None => new(0, 0);

    public LotRedating Add(LotRedating other) => new(Redated + other.Redated, Skipped + other.Skipped);
}
