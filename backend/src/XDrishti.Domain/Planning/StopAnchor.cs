namespace XDrishti.Domain.Planning;

/// <summary>What a stored stop level is measured from (spec P7-19, ADR 0108).</summary>
public enum StopAnchor
{
    /// <summary>A market structure level (swing, bar) that does not depend on where the owner enters.</summary>
    Level,

    /// <summary>A percentage or ATR distance from the close of D; it moves with the real entry.</summary>
    CloseOfD,
}

/// <summary>Stored codes of <see cref="StopAnchor"/>.</summary>
public static class StopAnchorText
{
    /// <summary>The stored code.</summary>
    public static string Code(this StopAnchor anchor) => anchor == StopAnchor.Level ? "level" : "close_of_d";

    /// <summary>Parses a stored code; null when unknown.</summary>
    public static StopAnchor? TryParse(string? code) => code switch { "level" => StopAnchor.Level, "close_of_d" => StopAnchor.CloseOfD, _ => null };
}
