using XDrishti.Domain.Accounts;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Selection;
using XDrishti.Execution.Domain.Checks;

namespace XDrishti.Execution.Tests.Checks;

public sealed class PropertyAndParityTests
{
    [Fact]
    public void Any_doctored_hard_input_never_yields_cleared_over_5000_seeds_and_a_pass_on_unavailable_is_caught()
    {
        var rows = UnavailableTableTests.Rows().Where(r => r.Expected.Any(c => CheckCodes.ClassOf(c) == CheckClass.Hard)).ToArray();
        var random = new Random(12345);
        var caught = 0;
        for (var seed = 0; seed < 5000; seed++)
        {
            var f = Facts.Good();
            var picks = Enumerable.Range(0, random.Next(1, 4)).Select(_ => rows[random.Next(rows.Length)]).ToArray();
            foreach (var row in picks)
            {
                f = row.Break(f);
            }

            var run = f.Run();
            run.Verdict.ShouldBe(CheckVerdict.Rejected, "seed " + seed);
            run.Results.Select(r => r.Code).ShouldBe(CheckCodes.All);

            // Negative control: a judge that counts Unavailable as a pass would clear some of these runs.
            var mutantClears = run.Results.All(r => r.Outcome != CheckOutcome.Fail);
            if (mutantClears)
            {
                caught++;
            }
        }

        caught.ShouldBeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void Adding_a_failing_hard_check_never_turns_rejected_into_cleared()
    {
        var random = new Random(777);
        for (var seed = 0; seed < 500; seed++)
        {
            var rows = UnavailableTableTests.Rows().ToArray();
            var f = rows[random.Next(rows.Length)].Break(Facts.Good());
            var before = f.Run().Verdict;
            var after = (f with { S = f.S with { LiveHalt = Domain.HaltReading.Halted } }).Run().Verdict;
            (before == CheckVerdict.Rejected).ShouldBeTrue();
            after.ShouldBe(CheckVerdict.Rejected);
        }

    }

    // ---- parity with the planning checklist (P7-05) -------------------------------------------------------------------------------------------------------

    private static CheckOutcome Map(FilterOutcome o) => o switch
    {
        FilterOutcome.Pass => CheckOutcome.Pass,
        FilterOutcome.Fail => CheckOutcome.Fail,
        _ => CheckOutcome.Unavailable,
    };

    private static double? Maybe(Random r, double value) => r.Next(8) == 0 ? null : value;

    private static bool? MaybeBool(Random r) => r.Next(6) == 0 ? null : r.Next(2) == 0;

    [Fact]
    public void The_shared_checks_agree_with_the_planning_checklist_on_60_generated_rows()
    {
        var random = new Random(2026);
        var compared = new HashSet<string>();
        for (var row = 0; row < 60; row++)
        {
            var sector = random.Next(2) == 0 ? "IT" : null;
            var sectorPositions = random.Next(0, 4);
            var maxPerSector = random.Next(1, 4);
            var openTrade = MaybeBool(random);
            var days = random.Next(7) == 0 ? (int?)null : random.Next(0, 8);
            var ban = MaybeBool(random);
            var surveillance = MaybeBool(random);
            var dq = MaybeBool(random);
            var adv = Maybe(random, random.Next(1, 5000) * 100d);
            var correlation = Maybe(random, Math.Round((random.NextDouble() * 2) - 1, 2));
            var qty = 10 + random.Next(0, 5);
            var capital = 500_000d;
            var openRiskPct = Math.Round(random.NextDouble() * 3.4, 2);
            var riskPct = 15d * qty / capital * 100;

            var theirs = PreTradeChecklist.Evaluate(
                new ChecklistTicket("TCS", "BUY", 1500, qty, riskPct, sector),
                new ChecklistAccount("a", TradingMode.Paper, true, 0.5, 3, maxPerSector, 1.5, 100_000, openRiskPct, sectorPositions, 0.2),
                new ChecklistFacts(dq, null, openTrade, days, ban, surveillance, 1350, 1650, adv, correlation),
                ChecklistLimits.Default, ChecklistPolicy.Default).Checks.ToDictionary(c => c.Code, c => Map(c.Outcome));

            var f = Facts.Good();
            f = f with
            {
                I = f.I with { Quantity = qty, Ticket = new TicketFacts(TicketState.Armed, "owner", false, qty) },
                A = f.A with
                {
                    Sector = sector,
                    SectorPositions = sectorPositions,
                    OpenTradeExists = openTrade,
                    Profile = f.A.Profile! with { MaxPerSector = maxPerSector, MaxTotalRiskPct = 3 },
                    IntentOpenRiskInr = openRiskPct / 100 * capital,
                },
                M = f.M with { DaysToResults = days, InFnoBan = ban, UnderSurveillance = surveillance, DataQualityOk = dq, AverageDailyVolume = adv, MaxCorrelation = correlation },
            };
            var ours = f.Run().Results.ToDictionary(r => r.Code, r => r.Outcome);

            foreach (var (theirCode, ourCode) in new[]
            {
                (ChecklistCodes.SectorExposure, CheckCodes.SectorExposure), (ChecklistCodes.HoldingsConflict, CheckCodes.HoldingsConflict), (ChecklistCodes.Liquidity, CheckCodes.AdvShare),
                (ChecklistCodes.ResultsWithinDays, CheckCodes.ResultsWithinDays), (ChecklistCodes.FnoBan, CheckCodes.FnoBan), (ChecklistCodes.Surveillance, CheckCodes.Surveillance),
                (ChecklistCodes.Correlation, CheckCodes.Correlation), (ChecklistCodes.DataQuality, CheckCodes.DataQuality),
            })
            {
                ours[ourCode].ShouldBe(theirs[theirCode], $"row {row}: {ourCode}");
                compared.Add(ourCode);
            }

            // Exposure: open risk percent plus this trade against the total limit (the same risk of this order, in percent of capital).
            var theirExposure = theirs[ChecklistCodes.ExposureHeadroom];
            if (Math.Abs(openRiskPct + riskPct - 3) > 0.001)
            {
                ours[CheckCodes.ExposureHeadroom].ShouldBe(theirExposure, $"row {row}: exposure ({openRiskPct} %)");
                compared.Add(CheckCodes.ExposureHeadroom);
            }
        }

        compared.Count.ShouldBe(9);
    }
}
