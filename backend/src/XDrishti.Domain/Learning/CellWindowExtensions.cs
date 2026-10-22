namespace XDrishti.Domain.Learning;

/// <summary>Names and lengths of the windows.</summary>
public static class CellWindowExtensions
{
    /// <summary>The calendar days of the window, or null for all.</summary>
    public static int? Days(this CellWindow window) => window switch
    {
        CellWindow.All => null,
        CellWindow.Days250 => 250,
        CellWindow.Days60 => 60,
        _ => throw new ArgumentOutOfRangeException(nameof(window)),
    };

    /// <summary>The code stored and shown: <c>all</c>, <c>250d</c>, <c>60d</c>.</summary>
    public static string Code(this CellWindow window) => window switch
    {
        CellWindow.All => "all",
        CellWindow.Days250 => "250d",
        CellWindow.Days60 => "60d",
        _ => throw new ArgumentOutOfRangeException(nameof(window)),
    };

    /// <summary>The window of a code, or null.</summary>
    public static CellWindow? Parse(string? code) => code switch
    {
        "all" => CellWindow.All,
        "250d" => CellWindow.Days250,
        "60d" => CellWindow.Days60,
        _ => null,
    };
}
