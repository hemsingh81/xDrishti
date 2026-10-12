namespace XDrishti.Domain.Market.Actions;

/// <summary>A day bar as the detection reads it: the India date and the prices and volume of the day.</summary>
public sealed record DailyBar(DateOnly Date, double Open, double High, double Low, double Close, long Volume);
