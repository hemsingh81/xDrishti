using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>The ids derived from a registration: its look's subject key and the ids of its two arm results.</summary>
internal static class HoldOutRegistrationIds
{
    /// <summary>The version of the look row (one per registration).</summary>
    public const int LookVersion = 1;

    /// <summary>The subject id of the registration's look in <c>lab.holdout_unlocks</c>.</summary>
    public static string LookKey(Guid registrationId) => "system-replay:" + registrationId.ToString("N");

    /// <summary>The run id stored on the look row.</summary>
    public static Guid LookId(Guid registrationId) => new(SHA256.HashData(Encoding.UTF8.GetBytes("system-look|" + registrationId.ToString("N")))[..16]);

    /// <summary>The id of one arm's result (<c>selection</c> or <c>baseline</c>).</summary>
    public static Guid ArmId(Guid registrationId, string arm) => new(SHA256.HashData(Encoding.UTF8.GetBytes("system-arm|" + registrationId.ToString("N") + "|" + arm))[..16]);
}
