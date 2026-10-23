namespace XDrishti.Domain.Selection;

/// <summary>The one reading of a side: exactly <c>BUY</c> (+1) or <c>SELL</c> (-1); anything else (other case, abbreviations, blank) is 0, unknown, and never treated as the opposite side.</summary>
internal static class SideSign
{
    /// <summary>+1 for BUY, -1 for SELL, 0 for anything else.</summary>
    internal static int Of(string? side) => side switch { "BUY" => 1, "SELL" => -1, _ => 0 };
}
