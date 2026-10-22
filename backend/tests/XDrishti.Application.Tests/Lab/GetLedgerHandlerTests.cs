using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Lab;

/// <summary>P5-03 criteria 3, 4 and 9: the ledger read with its variant count and the factor in force.</summary>
public sealed class GetLedgerHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IStrategyStore _strategies = Substitute.For<IStrategyStore>();

    private readonly ITestLedger _ledger = Substitute.For<ITestLedger>();

    private static LedgerRow Row(string hash) => new(Guid.NewGuid(), new string('k', 64), "FAM", 1, hash, null, "symbols as listed", new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), "quick", DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData(true, 4, 1.5)]
    [InlineData(false, 4, 1.0)]
    [InlineData(true, 0, 1.0)]
    public async Task The_view_carries_rows_variant_count_and_the_factor_in_force(bool adjust, int variants, double factor)
    {
        _strategies.GetAsync("FAM", Ct).Returns(new StoredStrategy("FAM", "intraday", false, DateTimeOffset.UnixEpoch, 1, 1));
        _ledger.CountVariantsAsync("FAM", Ct).Returns(variants);
        _ledger.ListAsync("FAM", Ct).Returns([Row(new string('a', 64)), Row(new string('a', 64))]);
        var handler = new GetLedgerHandler(_strategies, _ledger, Settings(adjust));

        var result = await handler.HandleAsync(new GetLedgerQuery("FAM"), Ct);

        result.Value.VariantCount.ShouldBe(variants);
        result.Value.AdjustmentFactor.ShouldBe(factor, 1e-12);
        result.Value.Rows.Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_unknown_family_is_not_found()
    {
        _strategies.GetAsync("NOPE", Ct).Returns((StoredStrategy?)null);

        var result = await new GetLedgerHandler(_strategies, _ledger, LabSettings.Default).HandleAsync(new GetLedgerQuery("NOPE"), Ct);

        result.Error.Code.ShouldBe(LabErrors.NotFoundCode);
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    private static LabSettings Settings(bool adjust) => LabSettings.Default with { MultipleTesting = adjust };
}
