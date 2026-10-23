using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>The phase check as the verb prints it (P6-18).</summary>
/// <param name="Report">The criteria and the limits.</param>
/// <param name="Text">The printable report, limits included.</param>
public sealed record PhaseCheckView(PhaseCheckReport Report, string Text);
