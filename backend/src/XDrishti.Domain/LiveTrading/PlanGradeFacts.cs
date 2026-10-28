namespace XDrishti.Domain.LiveTrading;

/// <summary>The plan grade of a ticket as the outcome reads it: <c>pending</c> (not stored yet), <c>graded</c> (with its R), <c>not_triggered</c> or <c>not_gradable</c>. The stored word is also kept on the outcome row.</summary>
public sealed record PlanGradeFacts(string Status, double? R)
{
    public const string Pending = "pending";
    public const string Graded = "graded";
    public const string NotTriggered = "not_triggered";
    public const string NotGradable = "not_gradable";

    public static IReadOnlyList<string> All { get; } = [Pending, Graded, NotTriggered, NotGradable];
}
