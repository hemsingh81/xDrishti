using System.Text.Json;
using XDrishti.Domain.Market.Bars;
using XDrishti.Infrastructure.Persistence.Configurations;

namespace XDrishti.Infrastructure.Tests.Market.Bars;

public sealed class DataJobJsonTests
{
    [Fact]
    public void A_dhan_phase_stored_before_the_gap_counters_existed_reads_back_with_zero_gaps()
    {
        const string Stored =
            """{"status":"succeeded","startedAt":null,"finishedAt":null,"done":3,"total":3,"message":null,"code":null,"accountName":"Primary","instrumentsFetched":3,"instrumentsFailed":0,"requests":3,"bars":1125,"inserted":1125,"updated":0,"unchanged":0,"repaired":0,"rejected":0,"overlapCompared":0,"overlapMismatched":0,"errors":[]}""";

        var phase = JsonSerializer.Deserialize<DhanPhase>(Stored, DataJobConfiguration.Json)!;

        (phase.Status, phase.Inserted, phase.GapsFound, phase.GapsFilled, phase.GapsUnfillable).ShouldBe(("succeeded", 1125L, 0L, 0L, 0L));
    }

    [Fact]
    public void The_gap_counters_round_trip_in_camel_case()
    {
        var phase = DhanPhase.Empty(PhaseStatus.Succeeded) with { GapsFound = 5, GapsFilled = 3, GapsUnfillable = 1 };

        var json = JsonSerializer.Serialize(phase, DataJobConfiguration.Json);

        json.ShouldContain("\"gapsFound\":5");
        var back = JsonSerializer.Deserialize<DhanPhase>(json, DataJobConfiguration.Json)!;
        (back.GapsFound, back.GapsFilled, back.GapsUnfillable).ShouldBe((5L, 3L, 1L));
    }

    [Fact]
    public void The_default_of_the_quality_column_reads_back_as_a_phase_that_was_not_requested()
    {
        var json = DataJobConfiguration.QualityNotRequested[1..^"'::jsonb".Length]; // the SQL literal without its quotes and cast

        var phase = JsonSerializer.Deserialize<QualityPhase>(json, DataJobConfiguration.Json)!;

        phase.ShouldBe(QualityPhase.Empty(PhaseStatus.NotRequested));
    }

    [Fact]
    public void The_quality_phase_round_trips_in_camel_case()
    {
        var runId = Guid.NewGuid();
        var phase = QualityPhase.Empty(PhaseStatus.Succeeded) with { RunId = runId, Done = 214, Total = 214, Passed = 200, Warned = 10, Failed = 4 };

        var json = JsonSerializer.Serialize(phase, DataJobConfiguration.Json);

        json.ShouldContain("\"runId\":\"" + runId + "\"");
        JsonSerializer.Deserialize<QualityPhase>(json, DataJobConfiguration.Json).ShouldBe(phase);
    }
}
