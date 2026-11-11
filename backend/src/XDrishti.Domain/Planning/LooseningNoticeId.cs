using System.Globalization;

namespace XDrishti.Domain.Planning;

/// <summary>The notice id of a loosening: the source and the id of its change row, <c>sizing:7</c>, <c>budget:3</c> or <c>basket:12</c> (BK-10b).</summary>
public static class LooseningNoticeId
{
    /// <summary>The sizing settings table.</summary>
    public const string Sizing = "sizing";

    /// <summary>The basket risk budgets table.</summary>
    public const string Budget = "budget";

    /// <summary>The basket settings table.</summary>
    public const string Basket = "basket";

    /// <summary>The notice id of a change row.</summary>
    public static string Format(string source, long id) => string.Create(CultureInfo.InvariantCulture, $"{source}:{id}");

    /// <summary>The source and the row id of a notice id; null when it is not exactly one of the three forms with a positive whole id.</summary>
    public static (string Source, long Id)? Parse(string? noticeId)
    {
        var parts = noticeId?.Split(':');
        if (parts is not [var source, var number] || source is not (Sizing or Budget or Basket))
        {
            return null;
        }

        return number.Length > 0 && number.All(char.IsAsciiDigit) && long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? (source, id) : null;
    }
}
