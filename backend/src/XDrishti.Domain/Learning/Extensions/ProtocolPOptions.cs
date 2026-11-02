using System.Globalization;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The pre-registered knobs of protocol P (P15-00, P15-10); the defaults are the rule.</summary>
public sealed record ProtocolPOptions
{
    /// <summary>Seasons with computable events needed in all: choice, test and hold-out (20).</summary>
    public int RequiredSeasons { get; init; } = 20;

    /// <summary>Seasons that choose the variant (8).</summary>
    public int ChoiceSeasons { get; init; } = 8;

    /// <summary>Test seasons needed (8).</summary>
    public int MinTestSeasons { get; init; } = 8;

    /// <summary>Matched top-bucket events needed in the test (250).</summary>
    public int MinTopEvents { get; init; } = 250;

    /// <summary>The lower bound of the gain of the top bucket over the middle, before the factor (0.05 R).</summary>
    public double MinGainR { get; init; } = 0.05;

    /// <summary>The share of test seasons with a positive gain (2/3).</summary>
    public double PositiveSeasonShare { get; init; } = 2.0 / 3.0;

    /// <summary>Sessions of embargo after a phase ends (5).</summary>
    public int EmbargoSessions { get; init; } = 5;

    /// <summary>Bootstrap draws (10,000).</summary>
    public int Draws { get; init; } = 10_000;

    /// <summary>Bootstrap seed (1337).</summary>
    public ulong Seed { get; init; } = 1337;

    /// <summary>The one-sided level (0.90).</summary>
    public double Level { get; init; } = 0.90;

    /// <summary>Matched top-bucket events the hold-out needs (30).</summary>
    public int MinHoldOutEvents { get; init; } = 30;

    /// <summary>The problems with the values, empty when usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (ChoiceSeasons < 1 || MinTestSeasons < 1 || RequiredSeasons < ChoiceSeasons + MinTestSeasons + SeasonFolds.HoldOutSeasons)
        {
            problems.Add("required_seasons must cover the choice, the test and the four hold-out seasons");
        }

        if (MinTopEvents < 1 || MinHoldOutEvents < 1 || EmbargoSessions is < 0 or > 100)
        {
            problems.Add("min_top_events and min_hold_out_events 1 or more; embargo_sessions 0 to 100");
        }

        if (!double.IsFinite(MinGainR) || MinGainR < 0 || !double.IsFinite(PositiveSeasonShare) || PositiveSeasonShare is <= 0 or > 1)
        {
            problems.Add("min_gain_r not negative; positive_season_share above 0 and at most 1");
        }

        if (Draws is < 100 or > 1_000_000 || !double.IsFinite(Level) || Level is <= 0.5 or >= 1)
        {
            problems.Add("draws 100 to 1000000 and level above 0.5 and below 1");
        }

        return problems;
    }

    /// <summary>The options as text, for the recipe hash of the record.</summary>
    public string Canonical() => string.Create(CultureInfo.InvariantCulture, $"protocol-p/1;seasons={RequiredSeasons},{ChoiceSeasons},{MinTestSeasons};top={MinTopEvents};gain={MinGainR:R};positive={PositiveSeasonShare:R};embargo={EmbargoSessions};boot={Draws},{Seed},{Level:R};holdout={MinHoldOutEvents}");
}
