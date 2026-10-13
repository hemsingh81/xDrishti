namespace XDrishti.Application.Portfolio.Actions;

/// <summary>What the owner can do about an item (the buttons of the Action center); <c>snooze</c> and <c>done</c> are decisions, the others open a drawer or a dialog.</summary>
public static class ActionSuggestions
{
    public const string Open = "open";

    public const string SetAlert = "set_alert";

    public const string AddToBasket = "add_to_basket";

    public const string Snooze = "snooze";

    public const string Done = "done";
}
