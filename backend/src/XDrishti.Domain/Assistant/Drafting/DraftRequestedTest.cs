namespace XDrishti.Domain.Assistant.Drafting;

/// <summary>The test the idea asked for, as text only. The owner picks the window and the universe at Confirm.</summary>
public sealed record DraftRequestedTest(string? From, string? To, IReadOnlyList<string> Baskets)
{
    public static DraftRequestedTest None { get; } = new(null, null, []);
}
