namespace XDrishti.Domain.LiveTrading;

/// <summary>The entry orders of a scope by class (spec P11-02). <see cref="Counted"/> = full + partial + rejected + queue miss; price misses and unclassified orders are reported and never counted.</summary>
public sealed record CapacityCounts(int Full, int Partial, int Rejected, int QueueMiss, int PriceMiss, int Unclassified)
{
    /// <summary>The denominator of the capacity score.</summary>
    public int Counted => Full + Partial + Rejected + QueueMiss;

    /// <summary>Counts from the stored class words.</summary>
    public static CapacityCounts From(IEnumerable<string> classes)
    {
        ArgumentNullException.ThrowIfNull(classes);
        int full = 0, partial = 0, rejected = 0, queue = 0, price = 0, other = 0;
        foreach (var c in classes)
        {
            switch (c)
            {
                case OrderFillClass.Full: full++; break;
                case OrderFillClass.Partial: partial++; break;
                case OrderFillClass.Rejected: rejected++; break;
                case OrderFillClass.QueueMiss: queue++; break;
                case OrderFillClass.PriceMiss: price++; break;
                default: other++; break;
            }
        }

        return new CapacityCounts(full, partial, rejected, queue, price, other);
    }
}
