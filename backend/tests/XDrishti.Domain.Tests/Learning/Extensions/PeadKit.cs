using System.Globalization;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>Synthetic PEAD events (P15-10): per season 40 dates, each with one top-bucket and three middle events of one variant; sessions advance 100 per season, so a hold of 5 sessions never runs into the next season.</summary>
public static class PeadKit
{
    public const string Variant = "sue_yoy/5";

    public static List<PeadEvent> Events(int seasons, double effect, int seed, string variant = Variant, int datesPerSeason = 40, int skipVariantSeason = -1)
    {
        var random = new SeededRandom((ulong)seed + 5);
        var list = new List<PeadEvent>();
        for (var s = 0; s < seasons; s++)
        {
            if (s == skipVariantSeason)
            {
                list.Add(Event("filler", s, 0, 0, PeadBucket.Middle, "sue_yoy/20", 0));
                continue;
            }

            for (var d = 0; d < datesPerSeason; d++)
            {
                list.Add(Event("t", s, d, 0, PeadBucket.Top, variant, effect + Noise(random)));
                for (var m = 1; m <= 3; m++)
                {
                    list.Add(Event("m", s, d, m, PeadBucket.Middle, variant, Noise(random)));
                }
            }
        }

        return list;
    }

    public static PeadEvent Event(string tag, int season, int d, int m, PeadBucket bucket, string variant, double r)
    {
        var entry = (season * 100) + d;
        return new PeadEvent(string.Create(CultureInfo.InvariantCulture, $"{tag}|{season}|{d}|{m}|{variant}"), variant, new DateOnly(2018 + (season / 4), ((season % 4) * 3) + 1, 1).AddDays(d), entry, entry + 5, bucket, r);
    }

    private static double Noise(SeededRandom random) => (random.Next(2001) - 1000) / 1000.0;
}
