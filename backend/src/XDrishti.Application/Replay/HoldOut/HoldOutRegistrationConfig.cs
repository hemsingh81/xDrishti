using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Replay.HoldOut;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>
/// What a registration fixes before the block begins (SR-03 D1), stored as canonical JSON whose SHA-256 is the <c>config_hash</c>: the target block, the parameters of the selection arm (the baseline arm is the same with <c>arm = baseline</c>), the hash of the cost sheet's rates,
/// the whole settings snapshot (and its hash), the resolved scope hash, the strategy versions, the engine version and the pass rule. The run uses these stored inputs, never today's.
/// </summary>
/// <param name="BlockStart">The first date of the target block.</param>
/// <param name="BlockEnd">The last date of the target block.</param>
/// <param name="Parameters">The selection arm: range <c>[BlockStart, BlockEnd]</c>, scope, capital, profile, sheet id, version rule <c>start</c>, mode <c>frozen</c>.</param>
/// <param name="Arms">The arms: <c>selection</c> and <c>baseline</c>.</param>
/// <param name="CostSheetRatesHash">The rates hash of the checked sheet at registration.</param>
/// <param name="Settings">The settings snapshot at registration.</param>
/// <param name="SettingsHash">SHA-256 of the canonical snapshot.</param>
/// <param name="ScopeHash">The hash of the resolved scope at registration.</param>
/// <param name="Instruments">How many instruments the scope reads at registration.</param>
/// <param name="Versions">The strategy versions by strategy id.</param>
/// <param name="EngineVersion">The simulation engine version at registration.</param>
/// <param name="PassRule">The pass rule.</param>
public sealed record HoldOutRegistrationConfig(
    DateOnly BlockStart,
    DateOnly BlockEnd,
    ReplayParams Parameters,
    IReadOnlyList<string> Arms,
    string CostSheetRatesHash,
    ReplaySettingsSnapshot Settings,
    string SettingsHash,
    string ScopeHash,
    int Instruments,
    IReadOnlyDictionary<string, List<StoredVersion>> Versions,
    string EngineVersion,
    SystemReplayPassRule PassRule);
