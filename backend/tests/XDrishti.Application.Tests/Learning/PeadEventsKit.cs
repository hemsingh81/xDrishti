using System.Globalization;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Tests.Learning;

/// <summary>Synthetic PEAD events: per season 40 dates, each with one top-bucket and three middle events of the variant; sessions advance 100 per season.</summary>
internal static class PeadEventsKit
{
    public static List<PeadEvent> Events(int seasons, double effect)
    {
        var random = new SeededRandom(9);
        var list = new List<PeadEvent>();
        for (var s = 0; s < seasons; s++)
        {
            for (var d = 0; d < 40; d++)
            {
                list.Add(Event("t", s, d, 0, PeadBucket.Top, effect + Noise(random)));
                for (var m = 1; m <= 3; m++)
                {
                    list.Add(Event("m", s, d, m, PeadBucket.Middle, Noise(random)));
                }
            }
        }

        return list;
    }

    private static PeadEvent Event(string tag, int season, int d, int m, PeadBucket bucket, double r)
    {
        var entry = (season * 100) + d;
        return new PeadEvent(string.Create(CultureInfo.InvariantCulture, $"{tag}|{season}|{d}|{m}"), "sue_yoy/5", new DateOnly(2018 + (season / 4), ((season % 4) * 3) + 1, 1).AddDays(d), entry, entry + 5, bucket, r);
    }

    private static double Noise(SeededRandom random) => (random.Next(2001) - 1000) / 1000.0;
}
