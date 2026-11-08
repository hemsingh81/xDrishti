namespace XDrishti.Application.Confidence;

/// <summary>One graded forward (shadow and paper) trade as far as its liquidity tier needs it: the instrument's symbol (from the trade key) and the session it was entered on.</summary>
/// <param name="Symbol">The symbol, or null when the trade key has none.</param>
/// <param name="Session">The India date of the entry session.</param>
internal sealed record ForwardTrade(string? Symbol, DateOnly Session);
