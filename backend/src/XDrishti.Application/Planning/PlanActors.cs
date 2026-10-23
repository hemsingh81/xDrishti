namespace XDrishti.Application.Planning;

/// <summary>Who counts as the owner in the plan record (spec P7-03). Only <c>owner:...</c>; the database repeats it (<c>approvals.actor</c> starts with <c>owner</c>). The first write endpoint must build the actor from the authenticated identity, never from the request.</summary>
internal static class PlanActors
{
    /// <summary>The prefix of an owner actor.</summary>
    public const string OwnerPrefix = "owner:";

    /// <summary>Whether the actor is the owner.</summary>
    public static bool IsOwner(string actor) => actor.StartsWith(OwnerPrefix, StringComparison.Ordinal);
}
