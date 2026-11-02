using System.Globalization;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>A season of the P15 protocols (P15-10): the calendar quarter of a decision date. The independent unit of the filings protocols, not the trade.</summary>
public readonly record struct Season(int Year, int Quarter) : IComparable<Season>
{
    /// <summary>The season of the date.</summary>
    public static Season Of(DateOnly date) => new(date.Year, ((date.Month - 1) / 3) + 1);

    /// <summary>The first day of the season.</summary>
    public DateOnly Start => new(Year, ((Quarter - 1) * 3) + 1, 1);

    public int CompareTo(Season other) => Year != other.Year ? Year.CompareTo(other.Year) : Quarter.CompareTo(other.Quarter);

    public static bool operator <(Season left, Season right) => left.CompareTo(right) < 0;

    public static bool operator <=(Season left, Season right) => left.CompareTo(right) <= 0;

    public static bool operator >(Season left, Season right) => left.CompareTo(right) > 0;

    public static bool operator >=(Season left, Season right) => left.CompareTo(right) >= 0;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Year}Q{Quarter}");
}
