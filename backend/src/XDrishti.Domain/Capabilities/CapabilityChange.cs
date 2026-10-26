using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Capabilities;

/// <summary>
/// One switch of a capability (spec P8-18 Part A, ADR 0128): the only way a row of the log comes to be. Only an owner actor makes one (<c>owner:...</c>, taken from the signed-in identity by the endpoint, never from a request), for a capability of the whitelist,
/// with a reason. The type holds no clock and no id: the database stamps the time, the id and the snapshot hash.
/// </summary>
public sealed record CapabilityChange
{
    private CapabilityChange(string name, bool enabled, string actor, string reason)
    {
        Name = name;
        Enabled = enabled;
        Actor = actor;
        Reason = reason;
    }

    /// <summary>The capability.</summary>
    public string Name { get; }

    /// <summary>True to allow, false to stop allowing.</summary>
    public bool Enabled { get; }

    /// <summary>The owner who switched it.</summary>
    public string Actor { get; }

    /// <summary>Why, trimmed, 1 to 500 characters.</summary>
    public string Reason { get; }

    /// <summary>Makes the change, or says why not: an owner actor, a capability of the whitelist and a reason that fits.</summary>
    public static Result<CapabilityChange> Create(string? name, bool enabled, string? actor, string? reason)
    {
        if (!OwnerConfirmation.IsOwnerActor(actor) || actor!.Length > 100)
        {
            return Result.Failure<CapabilityChange>(CapabilityErrors.OwnerOnly);
        }

        if (!Capabilities.IsKnown(name))
        {
            return Result.Failure<CapabilityChange>(CapabilityErrors.NotFound(name));
        }

        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > Capabilities.MaxReasonLength || text.Any(char.IsControl))
        {
            return Result.Failure<CapabilityChange>(CapabilityErrors.Invalid("A switch needs a reason of 1 to 500 characters without control characters."));
        }

        return Result.Success(new CapabilityChange(name!, enabled, actor, text));
    }
}
