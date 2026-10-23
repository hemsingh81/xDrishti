namespace XDrishti.Domain.Planning;

/// <summary>The fixed sentences every plan carries as data (spec P7-03, ADR 0083). A test pins them.</summary>
public static class PlanWording
{
    /// <summary>Stored on every plan: armed is not an order.</summary>
    public const string NoOrder = "No order is placed by this system. Armed means the instrument is watched and alerts are raised; a stop is a level and an alert, not a protection. Any order is entered by hand in the broker's own app.";

    /// <summary>Stored on every plan: how far the numbers go.</summary>
    public const string HonestLimits = "Numbers are replayed history of about 2.1 years with modelled fills and no hold-out; a probability here is a shrunk estimate, not a forecast. Inputs that were missing are listed on the ticket as unavailable and never count as a pass.";
}
