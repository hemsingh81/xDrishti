using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The <c>Confidence:Ladder</c> settings of CF-10 part 10b (ADR 0215): the complexity ladder, the events-per-variable cap and the feature stability checks of a model, shown in the validation report. Every number is a proposal of the spec (D2, D3) and a setting, not a constant.
/// Nothing here decides anything: the evidence is printed and no verdict, eligibility rule or champion rule reads it (the predicate that will is part 10c), so there is no enforce switch and <see cref="ConfidenceMode.Gate"/> is refused.
/// <see cref="Problems"/> lists every value out of its range (the host refuses to start on one).
/// </summary>
/// <param name="Mode"><see cref="ConfidenceMode.Off"/> (nothing computed) or <see cref="ConfidenceMode.ReportOnly"/> (the default).</param>
/// <param name="Epv">Events per variable (default 15; the literature range is 10 to 20): a logistic model may have at most <c>floor(events / Epv)</c> features, where events are the smaller class.</param>
/// <param name="MaxLogisticFeatures">The most features of a logistic model whatever the events (default 8).</param>
/// <param name="MinTreeEvents">The fewest events for a tree model (default 100).</param>
/// <param name="MinProbability">The probability that a rung beats the rung below that earns it (default 0.95); the figure itself comes from the paired bootstrap (part 10c) and is "unavailable" until then.</param>
/// <param name="PsiWatch">The population stability index from which a feature is on watch (default 0.10).</param>
/// <param name="PsiAlarm">The index above which a feature is in alarm (default 0.25).</param>
/// <param name="PsiBins">Equal-mass bins of the index (default 10).</param>
/// <param name="MinRows">The fewest rows on each side for an index or a direction of one fold (default 30).</param>
/// <param name="MaxFlipShare">A feature whose direction flips in more than this share of the folds that give one would be dropped (default one third).</param>
public sealed record LadderOptions(
    ConfidenceMode Mode = ConfidenceMode.ReportOnly,
    int Epv = 15,
    int MaxLogisticFeatures = 8,
    int MinTreeEvents = 100,
    double MinProbability = 0.95,
    double PsiWatch = 0.10,
    double PsiAlarm = 0.25,
    int PsiBins = 10,
    int MinRows = 30,
    double MaxFlipShare = 1.0 / 3.0)
{
    /// <summary>True when anything is computed.</summary>
    public bool IsOn => Mode != ConfidenceMode.Off;

    /// <summary>The text of the values, for a stored document that must say which settings produced it.</summary>
    public string KeyText() => string.Create(CultureInfo.InvariantCulture, $"{Mode}/epv={Epv}/maxLogistic={MaxLogisticFeatures}/minTreeEvents={MinTreeEvents}/minP={MinProbability:R}/psi={PsiWatch:R}-{PsiAlarm:R}/bins={PsiBins}/minRows={MinRows}/flip={MaxFlipShare:R}");

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!Enum.IsDefined(Mode) || Mode == ConfidenceMode.Gate)
        {
            problems.Add("Ladder:Mode must be Off or ReportOnly (nothing here enforces)");
        }

        if (Epv is < 5 or > 50)
        {
            problems.Add("Ladder:Epv must be 5 to 50");
        }

        if (MaxLogisticFeatures is < 1 or > 50)
        {
            problems.Add("Ladder:MaxLogisticFeatures must be 1 to 50");
        }

        if (MinTreeEvents is < 10 or > 100000)
        {
            problems.Add("Ladder:MinTreeEvents must be 10 to 100000");
        }

        if (!double.IsFinite(MinProbability) || MinProbability is < 0.5 or > 0.999)
        {
            problems.Add("Ladder:MinProbability must be 0.50 to 0.999");
        }

        if (!double.IsFinite(PsiWatch) || !double.IsFinite(PsiAlarm) || PsiWatch <= 0 || PsiAlarm <= PsiWatch || PsiAlarm > 5)
        {
            problems.Add("Ladder:PsiWatch must be above 0 and below Ladder:PsiAlarm, which is at most 5");
        }

        if (PsiBins is < 2 or > 100)
        {
            problems.Add("Ladder:PsiBins must be 2 to 100");
        }

        if (MinRows is < 5 or > 100000)
        {
            problems.Add("Ladder:MinRows must be 5 to 100000");
        }

        if (!double.IsFinite(MaxFlipShare) || MaxFlipShare is <= 0 or >= 0.5)
        {
            problems.Add("Ladder:MaxFlipShare must be above 0 and below 0.5");
        }

        return problems.AsReadOnly();
    }
}
