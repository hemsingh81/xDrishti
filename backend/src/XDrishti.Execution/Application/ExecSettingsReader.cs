using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application;

/// <summary>The newest complete settings snapshot (key to text), or null when none was written yet.</summary>
public interface IExecSettingsSnapshots
{
    Task<IReadOnlyDictionary<string, string>?> NewestAsync(CancellationToken cancellationToken);
}

/// <summary>The values in force and the keys whose stored value was replaced by the default (out of range or unparsable) so that the host can log them.</summary>
public sealed record ExecSettingsView(IReadOnlyDictionary<string, string> Values, IReadOnlyList<string> Replaced);

/// <summary>Per registry key, the newest snapshot's value merged over the default. An unknown key is ignored; an invalid value is replaced by the default; no snapshot means all defaults.</summary>
public sealed class ExecSettingsReader(IExecSettingsSnapshots snapshots)
{
    public async Task<ExecSettingsView> ReadAsync(CancellationToken cancellationToken)
    {
        var snapshot = await snapshots.NewestAsync(cancellationToken);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        List<string> replaced = [];
        foreach (var setting in ExecSettingRegistry.All)
        {
            if (snapshot is not null && snapshot.TryGetValue(setting.Key, out var stored))
            {
                if (ExecSettingRegistry.IsValid(setting, stored))
                {
                    values[setting.Key] = stored;
                    continue;
                }

                replaced.Add(setting.Key);
            }

            values[setting.Key] = setting.Default;
        }

        return new ExecSettingsView(values, replaced);
    }
}
