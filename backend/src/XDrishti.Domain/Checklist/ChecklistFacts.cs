namespace XDrishti.Domain.Checklist;

/// <summary>What is known about the instrument and the book, as of the decision. Null means not known, and the check says unavailable. Results dates and the F&amp;O ban list have no data source yet (P15), so they are null today.</summary>
/// <param name="DataQualityOk">The latest validation status of the instrument is clean.</param>
/// <param name="HeldLongAnyAccount">The instrument is held long in any account.</param>
/// <param name="OpenTradeExists">A trade in the instrument is already open.</param>
/// <param name="DaysToResults">Days until the next quarterly results; null when not known; negative is invalid and unavailable.</param>
/// <param name="InFnoBan">The instrument is in the F&amp;O ban list.</param>
/// <param name="UnderSurveillance">The instrument is under ASM / GSM.</param>
/// <param name="PriceBandLower">Lower circuit price.</param>
/// <param name="PriceBandUpper">Upper circuit price.</param>
/// <param name="AverageDailyVolume">Average daily volume in shares.</param>
/// <param name="MaxCorrelation">Highest correlation with open trades and approved tickets, in the direction of the bets (P7-06 computes it).</param>
public sealed record ChecklistFacts(
    bool? DataQualityOk = null,
    bool? HeldLongAnyAccount = null,
    bool? OpenTradeExists = null,
    int? DaysToResults = null,
    bool? InFnoBan = null,
    bool? UnderSurveillance = null,
    double? PriceBandLower = null,
    double? PriceBandUpper = null,
    double? AverageDailyVolume = null,
    double? MaxCorrelation = null);
