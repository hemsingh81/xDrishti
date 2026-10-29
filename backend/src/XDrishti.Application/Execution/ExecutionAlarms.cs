using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Execution;

/// <summary>Reads the alarm record of the execution service (spec P12-04, criterion 11): read only, newest first, at most 200. <see cref="Open"/> keeps only alarms with no clear row.</summary>
public sealed record GetExecutionAlarmsQuery(bool Open, Guid? AccountId, int Take) : IQuery<Result<IReadOnlyList<ExecutionAlarmView>>>;

/// <summary>One alarm. Severity is <c>info</c>, <c>warn</c> or <c>critical</c>; the intent is null for an alarm about no single intent; ClearedAt and ClearedBy are null while the alarm is open.</summary>
public sealed record ExecutionAlarmView(long Id, DateTimeOffset At, string Code, string Severity, Guid? AccountId, Guid? IntentPublicId, string Detail, DateTimeOffset? ClearedAt, string? ClearedBy);

public interface IExecutionAlarmReader
{
    Task<IReadOnlyList<ExecutionAlarmView>> ReadAsync(bool open, Guid? accountId, int take, CancellationToken cancellationToken);
}

internal sealed class GetExecutionAlarmsHandler(IExecutionAlarmReader reader) : IQueryHandler<GetExecutionAlarmsQuery, Result<IReadOnlyList<ExecutionAlarmView>>>
{
    public const int MaxTake = 200;

    public async ValueTask<Result<IReadOnlyList<ExecutionAlarmView>>> HandleAsync(GetExecutionAlarmsQuery query, CancellationToken cancellationToken)
    {
        if (query.Take is < 1 or > MaxTake)
        {
            return Result.Failure<IReadOnlyList<ExecutionAlarmView>>(Error.Validation("execution.alarms_take", $"take must be 1 to {MaxTake}."));
        }

        return Result.Success(await reader.ReadAsync(query.Open, query.AccountId, query.Take, cancellationToken));
    }
}
