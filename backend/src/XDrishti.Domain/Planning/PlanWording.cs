namespace XDrishti.Domain.Planning;

/// <summary>The fixed sentences every plan carries as data (spec P7-03, ADR 0083). A test pins them.</summary>
public static class PlanWording
{
    /// <summary>Stored on every plan: armed is not an order.</summary>
    public const string NoOrder = "No order is placed by this system. Armed means the instrument is watched and alerts are raised; a stop is a level and an alert, not a protection. Any order is entered by hand in the broker's own app.";

    /// <summary>Starts the basis of every plan the selection builder publishes (P7-09, ADR 0092): a proposal, provisional, no order exists.</summary>
    public const string Proposed = "Proposed, provisional: no order exists, nothing here is approved.";

    /// <summary>The words the plan carries when the owner setting says the open book is empty (P7-09): shown in the basis and on every primary ticket.</summary>
    public const string OpenBookAssumedEmpty = "Open book: assumed empty by owner setting (positions held elsewhere are not counted).";

    /// <summary>The words the plan starts its open-book flag with when the book is not known (the default): the fill takes nothing. The suggestions rule reads it.</summary>
    public const string OpenBookUnavailableMarker = "Open book unavailable";

    /// <summary>The words the market-gate banner flag starts with when the gate has no data (the smart rules write it, the suggestions rule reads it; a test pins both).</summary>
    public const string GateUnavailableMarker = "market gate unavailable";

    /// <summary>Stored on every plan: how far the numbers go.</summary>
    public const string HonestLimits = "Numbers are replayed history of about 2.1 years with modelled fills and no hold-out; a probability here is a shrunk estimate, not a forecast. Inputs that were missing are listed on the ticket as unavailable and never count as a pass.";
}
