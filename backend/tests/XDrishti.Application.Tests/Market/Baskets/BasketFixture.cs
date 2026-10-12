using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Tracking;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Tests.Market.Baskets;

/// <summary>Builds the basket handlers on in-memory ports and a controllable clock; the master list and tracking are those of <see cref="TrackingFixture"/>'s data.</summary>
internal sealed class BasketFixture
{
    public const string Owner = TrackingFixture.Owner;

    public FakeTimeProvider Clock { get; } = new(MasterFixtures.Now);

    public InMemoryTrackingData Master { get; } = new();

    public InMemoryBasketData Data { get; }

    public BasketFixture() => Data = new InMemoryBasketData(Master);

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public InstrumentTracking Tracking => new(Master, Master);

    public CreateBasketHandler Create => new(Data, Data, Data, Master, Clock, NullLogger<CreateBasketHandler>.Instance);

    public UpdateBasketHandler Update => new(Data, Data, Data, Master, Clock, NullLogger<UpdateBasketHandler>.Instance);

    public ArchiveBasketHandler Archive => new(Data, Data, Master, Clock, NullLogger<ArchiveBasketHandler>.Instance);

    public ReorderBasketsHandler ReorderBaskets => new(Data, Data, Master, Clock, NullLogger<ReorderBasketsHandler>.Instance);

    public AddBasketMembersHandler Add => new(Data, Tracking, Data, Master, Clock, NullLogger<AddBasketMembersHandler>.Instance);

    public RemoveBasketMembersHandler Remove => new(Data, Data, Master, Clock, NullLogger<RemoveBasketMembersHandler>.Instance);

    public ReorderBasketMembersHandler ReorderMembers => new(Data, Data, Master, Clock, NullLogger<ReorderBasketMembersHandler>.Instance);

    public TransferBasketMembersHandler Transfer => new(Data, Tracking, Data, Master, Clock, NullLogger<TransferBasketMembersHandler>.Instance);

    public GetBasketMembersHandler Members => new(Data);

    public GetBasketHistoryHandler History => new(Data);

    public ListInstrumentBasketsHandler ForInstrument => new(Data);

    public Basket Basket(string name = "Core", BasketPurpose purpose = BasketPurpose.Trading) => Data.AddBasket(name, Clock.GetUtcNow(), purpose);

    public Task<Result<AddMembersResult>> AddAsync(Basket basket, params Guid[] ids) =>
        Add.HandleAsync(new AddBasketMembersCommand(basket.Id, new AddMembersInput(ids, null), Owner), Ct).AsTask();

    public async Task<AddMembersResult> AddOkAsync(Basket basket, params Guid[] ids)
    {
        var result = await AddAsync(basket, ids);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    public Task<Result<RemoveMembersResult>> RemoveAsync(Basket basket, params Guid[] ids) =>
        Remove.HandleAsync(new RemoveBasketMembersCommand(basket.Id, new RemoveMembersInput(ids), Owner), Ct).AsTask();

    public Task<Result<TransferResult>> TransferAsync(Basket from, Basket to, string mode, params Guid[] ids) =>
        Transfer.HandleAsync(new TransferBasketMembersCommand(new TransferInput(from.Id, to.Id, ids, mode), Owner), Ct).AsTask();

    public Task<Result> ReorderMembersAsync(Basket basket, params Guid[] ids) =>
        ReorderMembers.HandleAsync(new ReorderBasketMembersCommand(basket.Id, new MembersOrderInput(ids), Owner), Ct).AsTask();

    /// <summary>Marks the instruments as tracked, as an earlier track call would have.</summary>
    public void Track(params Guid[] ids)
    {
        foreach (var id in ids)
        {
            Master.Tracked.Add(XDrishti.Domain.Market.TrackedInstrument.Track(id, Owner, Clock.GetUtcNow()));
        }
    }

    /// <summary>Master-list instruments with these symbols.</summary>
    public Guid[] Instruments(params string[] symbols) => [.. symbols.Select(s => Master.AddInstrument(s).Id)];
}
