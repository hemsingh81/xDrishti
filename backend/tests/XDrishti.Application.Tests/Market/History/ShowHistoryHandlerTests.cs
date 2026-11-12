#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.History;
using XDrishti.Application.Tests.Market.Bars;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.History;

namespace XDrishti.Application.Tests.Market.History;

/// <summary>CF-04d decision 10: <c>xd data history</c> shows, per instrument, whether its pre-seam bars would be admitted and the first reason when not.</summary>
public sealed class ShowHistoryHandlerTests
{
    private static readonly DateTimeOffset Seam = new(2024, 4, 2, 9, 15, 0, IndiaTime.Offset);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_row_says_yes_or_the_first_reason_and_an_instrument_without_a_seam_row_says_no_seam()
    {
        var targets = new FakeFetchTargets();
        var store = new FakeBackfillStore();
        var seams = new FakeSeams();
        var ok = new FetchTarget(Guid.NewGuid(), "AAA", "share", "1", "NSE_EQ", null);
        var none = new FetchTarget(Guid.NewGuid(), "BBB", "share", "2", "NSE_EQ", null);
        var unchecked_ = new FetchTarget(Guid.NewGuid(), "CCC", "share", "3", "NSE_EQ", null);
        targets.Targets.AddRange([ok, none, unchecked_]);
        var basis = SeamBasis.Of(Seam, [], null);
        var good = new SeamRecord(ok.InstrumentId, Seam, ProbeStatus.Passed, null, 1, 100, null, false, 0.8, basis, basis);
        var open = good with { InstrumentId = unchecked_.InstrumentId, ContinuityBasis = null };
        store.Records[ok.InstrumentId] = good;
        store.Records[unchecked_.InstrumentId] = open;
        seams.Facts[ok.InstrumentId] = new SeamAdmissionFacts(true, good, Seam, Seam, false);
        seams.Facts[unchecked_.InstrumentId] = new SeamAdmissionFacts(true, open, Seam, Seam, false);
        seams.Facts[none.InstrumentId] = new SeamAdmissionFacts(true, null, null, null, false);
        var adjustments = Substitute.For<IAdjustmentFactors>();
        adjustments.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(AdjustmentFactors.None);
        var handler = new ShowHistoryHandler(targets, store, seams, adjustments);

        var rows = await handler.HandleAsync(new ShowHistoryQuery(), Ct);

        rows.Select(r => (r.Symbol, r.Verified)).ShouldBe([("AAA", "yes"), ("BBB", "no_seam"), ("CCC", SeamVerification.ContinuityUnchecked)]);
    }
}
