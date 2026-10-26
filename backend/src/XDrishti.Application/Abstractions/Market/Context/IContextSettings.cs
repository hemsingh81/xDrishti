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
}
