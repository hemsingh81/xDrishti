using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>CF-01: the pooled out-of-sample trades carry their entry date as cluster into the evidence's dependence block; the numbers of the evidence itself do not move.</summary>
public sealed class OosEvidenceDependenceTests
{
    private static readonly ExitCandidate A = new("FIXED_RR", [new ExitParameterValue("rr", [2])]);

    private static DateOnly D(int month, int day) => new(2025, month, day);

    private static SelectionInput Input(IEnumerable<SelectionTrade> trades) => new("X 15m BUY intraday *", "RNet", false, "e", [A], [.. trades], [], new SelectionOptions());

    private static ExitSelection Selection(SelectionInput input) =>
        new(input, A.Id, null, null, 0, [new SelectionFold(1, D(7, 1), D(7, 31), null, 0, 0, 0, 0, A.Id, false, false, false, null, 0, null, false, [])], null, false, false, A.Id, null, [], new SelectionStability(null, string.Empty, 0, null, 0, 0, null, 0), []);

    private static (SelectionInput, ExitSelection) Cell()
    {
        // 18 trades on 6 entry sessions of 3, every session at its own level: strongly clustered
        var trades = Enumerable.Range(0, 18).Select(i => new SelectionTrade(A.Id, D(7, 1 + (i / 3)), D(7, 2 + (i / 3)), ((i / 3 % 2 == 0) ? 1.0 : -1.0) + (i % 3 * 0.1))).ToList();
        var input = Input(trades);
        return (input, Selection(input));
    }

    [Fact]
    public void Without_the_options_the_evidence_has_no_block()
    {
        OosEvidenceBuilder.Pool([Cell()], 0).Evidence.Dependence.ShouldBeNull();
        OosEvidenceBuilder.Pool([Cell()], 0, new ConfidenceOptions(Mode: ConfidenceMode.Off)).Evidence.Dependence.ShouldBeNull();
    }

    [Fact]
    public void With_report_only_the_block_is_filled_from_the_pooled_trades_by_entry_date_and_the_rest_is_the_same()
    {
        var plain = OosEvidenceBuilder.Pool([Cell()], 0).Evidence;

        var withBlock = OosEvidenceBuilder.Pool([Cell()], 0, new ConfidenceOptions()).Evidence;

        (withBlock with { Dependence = null }).ShouldBe(plain);
        withBlock.Dependence!.N.ShouldBe(18);
        withBlock.Dependence.Method.ShouldBe("cluster");
        withBlock.Dependence.NEff!.Value.ShouldBeLessThan(18);
        withBlock.Dependence.NEff!.Value.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void The_trade_list_for_the_robustness_checks_is_unchanged()
    {
        OosEvidenceBuilder.Trades([Cell()]).Count.ShouldBe(18);
    }
}
