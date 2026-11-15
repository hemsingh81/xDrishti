#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Confidence;
using XDrishti.Domain.Confidence;

namespace XDrishti.Application.Tests.Confidence;

/// <summary>GROSS-01 (ADR 0294): the forward edge is each trade's gross R less the cost buffer, labelled so, never net.</summary>
public sealed class ForwardEdgeReaderTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IShadowResultReader _reader = Substitute.For<IShadowResultReader>();
    private readonly IPaperLedgerData _ledger = Substitute.For<IPaperLedgerData>();

    public ForwardEdgeReaderTests() =>
        _ledger.EpochAsync(null, Arg.Any<CancellationToken>()).Returns(new EpochRow(1, Start, DateTimeOffset.UnixEpoch, "first", "default", "system"));

    /// <summary>One candidate a session, gross R alternating 0.30 and 0.10 (gross mean 0.20 R).</summary>
    private void Gross(int count) =>
        _reader.ResultsAsync(Arg.Any<CancellationToken>()).Returns([.. Enumerable.Range(0, count).Select(i => new ShadowResultRow(Start.AddDays(i), $"ORB|S{i}|BUY|{i}", "g", true, "graded", i % 2 == 0 ? 0.30 : 0.10, "gross", null, i % 2 == 0 ? 0.30 : 0.10, 1, InSet: true))]);

    private ValueTask<ForwardEdge> ReadAsync(double buffer) => new ForwardEdgeReader(_reader, _ledger, new CostBufferOptions(buffer)).ReadAsync("ORB", Ct);

    [Fact]
    public async Task A_gross_mean_of_a_fifth_r_less_a_tenth_buffer_is_a_tenth_and_labelled_gross_less_buffer()
    {
        Gross(40);

        var edge = await ReadAsync(0.10);

        edge.MeanR!.Value.ShouldBe(0.10, 1e-9);
        edge.SdR!.Value.ShouldBeGreaterThan(0);
        edge.PositiveMonthShare.ShouldBe(1.0);
        edge.CostBasis.ShouldBe("gross_less_buffer(0.10R)");
        edge.Basis.ShouldContain("Confidence:Costs:BufferR=0.1");
        edge.Basis.ShouldNotContain("net");
    }

    [Fact]
    public async Task A_quarter_r_buffer_makes_the_effect_negative()
    {
        Gross(40);

        var edge = await ReadAsync(0.25);

        edge.MeanR!.Value.ShouldBe(-0.05, 1e-9);
        edge.CostBasis.ShouldBe("gross_less_buffer(0.25R)");
        edge.PositiveMonthShare.ShouldBe(0.0);
    }

    [Fact]
    public async Task Fewer_than_two_trades_stay_unavailable()
    {
        Gross(1);

        var edge = await ReadAsync(0.10);

        (edge.Trades, edge.MeanR, edge.SdR, edge.PositiveMonthShare).ShouldBe((1, (double?)null, (double?)null, (double?)null));
    }
}
