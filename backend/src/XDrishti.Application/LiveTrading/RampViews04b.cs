using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.LiveTrading;

/// <summary>One evidence gate as the screen reads it. <c>number</c> is null when the state says it could not be measured.</summary>
public sealed record RampGateView(string Code, string State, double? Number, double? Threshold, int? N, string Reason);

/// <summary>One breach rule that fires now (B1 to B7) or still holds a hold.</summary>
public sealed record BreachView(string Code, string Action, string Reason);

/// <summary>A halt as the screen reads it: the period, the limit, what was measured under which state, and what it does (it is advice: the owner places every order).</summary>
public sealed record LiveHaltView(long Id, Guid AccountId, string Kind, DateOnly PeriodStart, double LimitR, double LimitPct, double? MeasuredR, double? MeasuredPct, string MeasureState, string Action, DateTimeOffset OpenedAt);

/// <summary>The loss of the last measured session: the state (value, realised_only, unavailable), the figures and what is left out.</summary>
public sealed record LiveLossView(DateOnly Date, string Health, string State, double? NetRupees, double? LossR, double? LimitR, double? OneRupees, long CarriedQty, int OpenUnmarked, string Note);

/// <summary>Reads the halts of an account (or all) in a range, newest first. Read only.</summary>
public sealed record GetLiveHaltsQuery(Guid? AccountId, DateOnly? From, DateOnly? To) : IQuery<Result<IReadOnlyList<LiveHaltView>>>;

/// <summary>A notice of the live record, derived on read: the words, the route and nothing to run.</summary>
public sealed record LiveNoticeView(string Id, string Kind, string Severity, string Text, string Route);

/// <summary>Reads the derived notices of the live record. Read only.</summary>
public sealed record GetLiveNoticesQuery : IQuery<IReadOnlyList<LiveNoticeView>>;

/// <summary>The owner clears a hold (spec P11-04). Refused while a breach condition still holds. Actor, session and fresh password are the endpoint's.</summary>
public sealed record ClearHoldCommand(string Actor, Guid AccountId, string? Reason) : ICommand;

/// <summary>The owner records that the small-live exit report was read and is met (the key to S3). Refused unless the report says MET right now.</summary>
public sealed record ConfirmExitCommand(string Actor, Guid AccountId, string? Reason) : ICommand;

/// <summary>The evaluation of the ramp and the halts (a step of the settle, and after each poll with <c>HaltsOnly</c>). Writes insert-only rows by <c>rule:ramp</c>; never an order.</summary>
public sealed record EvaluateRampCommand(Guid? AccountId, bool HaltsOnly) : ICommand;

/// <summary>What one evaluation did.</summary>
public sealed record RampEvaluationOutcome(int Accounts, int Holds, int StepDowns, int AutoStepUps, int HaltsOpened, int Failed, string Summary);
