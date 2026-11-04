using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Lab;

/// <summary>Registers a strategy version (spec CF-06, D1). <paramref name="Actor"/> is built from the signed-in session, never from a body.</summary>
public sealed record RegisterStrategyCommand(string StrategyId, int Version, string? Counterparty, string? ParameterSigns, string? Applicability, string? ExpectedEdge, string? Falsifier, string Actor) : ICommand;

/// <summary>The registration of a strategy version.</summary>
public sealed record GetRegistrationQuery(string StrategyId, int Version) : IQuery<Result<RegistrationView>>;

/// <summary>The one look at the hold-out for the succeeded run's version. <paramref name="Actor"/> is the owner after a fresh password.</summary>
public sealed record UnlockHoldOutCommand(Guid RunId, string Actor) : ICommand;

/// <summary>The locked hold-out: block, proposal and the looks taken.</summary>
public sealed record GetHoldOutQuery : IQuery<Result<HoldOutView>>;
