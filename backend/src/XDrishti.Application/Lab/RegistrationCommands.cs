using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Lab;

/// <summary>Registers a strategy version (spec CF-06, D1). <paramref name="Actor"/> is built from the signed-in session, never from a body.</summary>
public sealed record RegisterStrategyCommand(string StrategyId, int Version, string? Counterparty, string? ParameterSigns, string? Applicability, string? ExpectedEdge, string? Falsifier, string Actor) : ICommand;

/// <summary>The registration of a strategy version.</summary>
public sealed record GetRegistrationQuery(string StrategyId, int Version) : IQuery<Result<RegistrationView>>;

/// <summary>The one look at the hold-out for the succeeded run's version. <paramref name="Actor"/> is the owner after a fresh password.</summary>
public sealed record UnlockHoldOutCommand(Guid RunId, string Actor) : ICommand;

/// <summary>The owner's release of the hold-out block that starts on <paramref name="Start"/> (HO-01). <paramref name="Actor"/> is the owner after a fresh password; <paramref name="ForfeitLook"/> allows releasing a block with no fresh look, which forfeits its looks for good.</summary>
public sealed record ReleaseHoldOutCommand(DateOnly Start, bool ForfeitLook, string Actor) : ICommand;

/// <summary>The locked hold-out: block, proposal and the looks taken.</summary>
public sealed record GetHoldOutQuery : IQuery<Result<HoldOutView>>
{
    /// <summary>Whether the block in force reads what each kind of look can still do and what blocks its auto release (default true); a reader that needs only the looks taken (scorecard row 12) sets it false and skips the candidate scan.</summary>
    public bool Slots { get; init; } = true;
}
