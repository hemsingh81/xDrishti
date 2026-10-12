namespace XDrishti.Domain.Market.Baskets;

/// <summary>The checked, normalised attributes of a basket (name and description trimmed, colour in lower case).</summary>
public sealed record BasketDetails(string Name, string Colour, BasketPurpose Purpose, string? Description);
