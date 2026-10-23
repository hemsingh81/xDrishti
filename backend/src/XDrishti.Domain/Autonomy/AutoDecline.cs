namespace XDrishti.Domain.Autonomy;

/// <summary>One reason the rule left a ticket for the owner.</summary>
/// <param name="Code">A stable code, see <see cref="AutoDeclineCodes"/>; a check carries its code after a colon.</param>
/// <param name="Detail">The reason in words.</param>
public sealed record AutoDecline(string Code, string Detail);
