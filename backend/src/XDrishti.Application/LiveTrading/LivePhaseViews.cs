using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.LiveTrading;

/// <summary>The small-live phase check of one live account (spec P11-06). Both null: the one live account and the last closed session.</summary>
public sealed record LivePhaseCheckQuery(Guid? AccountId, DateOnly? Through) : IQuery<Result<LivePhaseCheckView>>;

/// <summary>The report and its deterministic text. <see cref="State"/> is <c>value</c>, or <c>empty</c> when there is no live session yet.</summary>
public sealed record LivePhaseCheckView(string State, LivePhaseReport Report, string Text);
