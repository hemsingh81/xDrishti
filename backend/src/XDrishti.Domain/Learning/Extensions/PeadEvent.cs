namespace XDrishti.Domain.Learning.Extensions;

/// <summary>One event of the PEAD study (P15-10): a position entered at the close of the reaction session, in one variant (measure and hold, for example <c>sue_yoy/5</c>).</summary>
/// <param name="Key">The event key.</param>
/// <param name="Variant">One of <see cref="ProtocolP.Variants"/>.</param>
/// <param name="EntryDate">The India date of the entry; its calendar quarter is the season.</param>
/// <param name="EntrySession">The index of the entry session in the trading calendar.</param>
/// <param name="ExitSession">The index of the exit session.</param>
/// <param name="Bucket">Top, or one of the middle three.</param>
/// <param name="R">The gross result in R.</param>
public sealed record PeadEvent(string Key, string Variant, DateOnly EntryDate, int EntrySession, int ExitSession, PeadBucket Bucket, double R);
