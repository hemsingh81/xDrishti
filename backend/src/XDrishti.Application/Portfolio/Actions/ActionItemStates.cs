namespace XDrishti.Application.Portfolio.Actions;

/// <summary>What the owner decided about an item, as the API names it: <c>open</c> (no decision, or the snooze is over), <c>snoozed</c>, <c>done</c>.</summary>
public static class ActionItemStates
{
    public const string Open = "open";

    public const string Snoozed = "snoozed";

    public const string Done = "done";
}
