using XDrishti.Domain.Common;

namespace XDrishti.Domain.Capabilities;

/// <summary>The refusals of the capability gate (spec P8-18 Part A).</summary>
public static class CapabilityErrors
{
    /// <summary>The code of <see cref="Invalid"/>.</summary>
    public const string InvalidCode = "capabilities.invalid";

    /// <summary>The write is switched off (the default). 403.</summary>
    public static Error Off(string name) => Error.Forbidden("capabilities.off", "This is switched off (" + name + "). Switch it on in the owner-act switches first; it is off by default.");

    /// <summary>No capability has that name. 404.</summary>
    public static Error NotFound(string? name) => Error.NotFound("capabilities.not_found", "No capability is called '" + Shorten(name) + "'.");

    /// <summary>The change is not acceptable (a reason that does not fit). 422.</summary>
    public static Error Invalid(string message) => Error.Validation(InvalidCode, message);

    /// <summary>Only the signed-in owner changes a capability. 403.</summary>
    public static Error OwnerOnly { get; } = Error.Forbidden("capabilities.owner_only", "Only the signed-in owner switches a capability.");

    private static string Shorten(string? name) => name is null ? string.Empty : new string([.. name.Take(40).Select(c => char.IsControl(c) ? '?' : c)]);
}
