namespace XDrishti.Application.Abstractions.Market.Context;

/// <summary>Configuration of the market context job (<c>Data:Context:*</c>, P3-01d).</summary>
public interface IContextSettings
{
    /// <summary>Whether the data job writes the context (<c>Data:Context:Enabled</c>, default true; with no tracked index it only reports what is missing).</summary>
    bool Enabled { get; }

    /// <summary>Canonical symbols of the regime index in order (<c>Data:Context:IndexSymbols</c>, default <c>NSE:NIFTY</c>).</summary>
    IReadOnlyList<string> IndexSymbols { get; }

    /// <summary>Canonical symbols of the volatility index in order (<c>Data:Context:VixSymbols</c>, default <c>NSE:INDIA_VIX</c>, <c>NSE:INDIAVIX</c>).</summary>
    IReadOnlyList<string> VixSymbols { get; }

    /// <summary>Sessions a day waits for its VIX leg (<c>Data:Context:VixGraceSessions</c>, default 5).</summary>
    int VixGraceSessions { get; }

    /// <summary>The share of the expected minutes a leg needs (<c>Data:Context:MinDayFraction</c>, default 0.96).</summary>
    double MinDayFraction { get; }

    /// <summary>Sessions the average of the breadth is taken over (<c>Data:Context:Breadth:SmaSessions</c>, default 20, 2 to 250).</summary>
    int BreadthSmaSessions { get; }

    /// <summary>The fewest voters for a breadth number (<c>Data:Context:Breadth:MinVoters</c>, default 50).</summary>
    int BreadthMinVoters { get; }

    /// <summary>The least share of the shares with a bar that must vote (<c>Data:Context:Breadth:MinShare</c>, default 0.5).</summary>
    double BreadthMinShare { get; }

    /// <summary>The least share of the universe that must have a bar on the day for a breadth number (<c>Data:Context:Breadth:MinBarShare</c>, default 0.9): a partial load is <c>insufficient</c>, so it is recomputed inside the grace and never frozen.</summary>
    double BreadthMinBarShare { get; }

    /// <summary>Sessions an <c>insufficient</c> breadth may still be recomputed after a late load of stock bars (<c>Data:Context:BreadthGraceSessions</c>, default 5).</summary>
    int BreadthGraceSessions { get; }
}
