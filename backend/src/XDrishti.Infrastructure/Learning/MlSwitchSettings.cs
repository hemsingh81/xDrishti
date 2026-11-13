using System.Data.Common;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary>
/// The three ML switches (spec CF-10 D6 to D8, part 10d-1, ADR 0220): the newest row of each name in <c>research.ml_switch_settings</c>, OFF when there is none (<see cref="IMlSwitchSettings"/>, and <see cref="IMlInfluenceSwitch"/> for <c>ml.influence.enabled</c>), and the append of
/// a change (<see cref="IMlSwitchStore"/>). Select and insert only; the database stamps the time and the id, drawn under a lock so that the highest id is the last to commit (the pattern of <c>confidence.kill_settings</c>), and refuses a name that is not one of the three.
/// <see cref="IsOnAsync"/> keeps the promise of its port: false whenever the switch cannot be read (a database failure is logged and reads as OFF; cancellation still cancels). <see cref="CurrentAsync"/> lets a failure through, so that an automatic act that needs its switch does nothing instead of guessing.
/// </summary>
internal sealed partial class MlSwitchSettings(NpgsqlDataSource dataSource, ILogger<MlSwitchSettings> logger) : IMlSwitchSettings, IMlSwitchStore, IMlInfluenceSwitch
{
    public async ValueTask<MlSwitches> CurrentAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT DISTINCT ON (name) name, id, enabled FROM research.ml_switch_settings ORDER BY name, id DESC");
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var found = new Dictionary<string, MlSwitchState>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            found[reader.GetString(0)] = new MlSwitchState(reader.GetInt64(1), reader.GetBoolean(2));
        }

        MlSwitchState Of(string name) => found.TryGetValue(name, out var state) ? state : new MlSwitchState(null, false);
        return new MlSwitches(Of(MlSwitch.Influence), Of(MlSwitch.AutoPromote), Of(MlSwitch.AutoRollback), Of(MlSwitch.AutoHoldOutScore));
    }

    public async ValueTask<bool> IsOnAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await CurrentAsync(cancellationToken)).Influence.On;
        }
        catch (DbException failure)
        {
            // fail closed: a switch that cannot be read is OFF
            InfluenceSwitchUnreadable(logger, failure.GetType().Name);
            return false;
        }
    }

    public async ValueTask<long> AppendAsync(string name, bool on, string actor, string reason, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("INSERT INTO research.ml_switch_settings (name, enabled, actor, reason) VALUES (@name, @enabled, @actor, @reason) RETURNING id");
        command.Parameters.AddWithValue("name", NpgsqlDbType.Varchar, name);
        command.Parameters.AddWithValue("enabled", NpgsqlDbType.Boolean, on);
        command.Parameters.AddWithValue("actor", NpgsqlDbType.Varchar, actor);
        command.Parameters.AddWithValue("reason", NpgsqlDbType.Varchar, reason);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    [LoggerMessage(EventId = 7208, Level = LogLevel.Error, Message = "learning.ml.switch: ml.influence.enabled could not be read ({ExceptionType}); it reads as OFF")]
    private static partial void InfluenceSwitchUnreadable(ILogger logger, string exceptionType);
}
