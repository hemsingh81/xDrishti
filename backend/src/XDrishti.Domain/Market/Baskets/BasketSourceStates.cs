namespace XDrishti.Domain.Market.Baskets;

/// <summary>States of a basket source (<c>basket_sources.state</c>); part of the API contract.</summary>
public static class BasketSourceStates
{
    public const string Never = "never";
    public const string Ok = "ok";
    public const string Stale = "stale";
    public const string Rejected = "rejected";
    public const string Failed = "failed";
}
