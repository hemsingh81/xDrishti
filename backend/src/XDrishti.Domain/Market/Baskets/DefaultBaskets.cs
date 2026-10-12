namespace XDrishti.Domain.Market.Baskets;

/// <summary>
/// The baskets the first migration creates empty (docs/design/06 §3). The migration holds its own copy of these values (a migration never changes);
/// a test keeps the two equal. The owner can rename or archive them like any other basket.
/// </summary>
public static class DefaultBaskets
{
    public static readonly DefaultBasket MyChoice = new(Guid.Parse("0199c1a0-0000-7000-8000-000000000001"), "MyChoice", "#6366f1", BasketPurpose.Trading, 0);

    public static readonly DefaultBasket MyLongTerm = new(Guid.Parse("0199c1a0-0000-7000-8000-000000000002"), "MyLongTerm", "#10b981", BasketPurpose.LongTerm, 1);

    public static readonly DefaultBasket BestStocks = new(Guid.Parse("0199c1a0-0000-7000-8000-000000000003"), "Best stocks", "#f59e0b", BasketPurpose.Trading, 2);

    public static IReadOnlyList<DefaultBasket> All { get; } = [MyChoice, MyLongTerm, BestStocks];
}

/// <summary>A basket created by the migration.</summary>
public sealed record DefaultBasket(Guid Id, string Name, string Colour, BasketPurpose Purpose, int Position);
