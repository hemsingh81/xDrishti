using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Replay.Reads;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Market;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Replay.HoldOut;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>What the owner (or the auto step) asks to register. Nulls are the defaults of a replay (the stored Auto scope, 10,00,000, the Standard profile); the sheet must be named by the owner and is the most recently checked one for the auto step.</summary>
public sealed record RegisterHoldOutCommand(
    IReadOnlyList<Guid>? BasketIds,
    IReadOnlyList<Guid>? InstrumentIds,
    decimal? Capital,
    Guid? RiskProfileId,
    string? CostSheetId,
    double? MaxDrawdownPct,
    string Mode,
    string Actor) : ICommand
{
    /// <summary>The owner's act (a session or a fresh password).</summary>
    public const string OwnerMode = "owner";

    /// <summary>The automatic switch, recorded as auto and never as an owner approval.</summary>
    public const string AutoMode = "auto";
}

/// <summary>The stored registration.</summary>
public sealed record RegisteredHoldOut(Guid Id, string ConfigHash, DateOnly BlockStart);

/// <summary>
/// Registers the system for the next block that has not begun (SR-03 D1, D2): resolves the scope, capital, profile and the checked sheet, takes the settings snapshot, the strategy versions and the engine version, hashes the canonical config and stores it, insert-only.
/// Refused with 422 <c>replay.registration_invalid</c> and a reason word: <c>no_future_block</c>, <c>gross_choice</c>, <c>sheet_missing</c>, <c>sheet_unchecked</c>, <c>scope_empty</c>, <c>scope_over_memory_cap</c>, <c>bad_rule</c>, <c>bad_mode</c>. Reads no bar.
/// </summary>
internal sealed class RegisterHoldOutHandler(
    IHoldOutStore blocks,
    IHoldOutRegistrationStore store,
    IReplayFacts facts,
    IReplayInputs inputs,
    ICostSheetStore sheets,
    IAutoScopeStore autoScope,
    IRiskProfileStore profiles,
    ISimulationEngineVersion engine,
    SystemReplayOptions replayOptions,
    HoldOutOptions holdOut,
    TimeProvider time) : ICommandHandler<RegisterHoldOutCommand, RegisteredHoldOut>
{
    public const string OwnerMode = RegisterHoldOutCommand.OwnerMode;
    public const string AutoMode = RegisterHoldOutCommand.AutoMode;

    public async ValueTask<Result<RegisteredHoldOut>> HandleAsync(RegisterHoldOutCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Mode is not (OwnerMode or AutoMode))
        {
            return Fail("bad_mode", "the mode is owner or auto");
        }

        if (string.IsNullOrWhiteSpace(command.Actor) || command.Actor.Length > 100)
        {
            return Fail("bad_mode", "a registration needs an actor of at most 100 characters");
        }

        var rule = command.MaxDrawdownPct is { } limit ? new SystemReplayPassRule(MaxDrawdownPct: limit) : SystemReplayPassRule.Default;
        if (rule.Problems() is { Count: > 0 } ruleProblems)
        {
            return Fail("bad_rule", string.Join("; ", ruleProblems));
        }

        var now = time.GetUtcNow();
        var stored = (await blocks.ListBlocksAsync(cancellationToken)).Select(b => (b.Block.Start, b.Block.HistoryLast)).ToList();
        if (SystemTarget.Pick(stored, holdOut.BlockMonths, IndiaTime.Today(now)) is not { } target)
        {
            return Fail("no_future_block", "every block of the chain has begun; a registration must be stored before its block begins, and block 1 never takes a system look");
        }

        var sheet = await ResolveSheetAsync(command, cancellationToken);
        if (sheet.IsFailure)
        {
            return Result.Failure<RegisteredHoldOut>(sheet.Error);
        }

        var scope = new ReplayScope([.. command.BasketIds ?? []], [.. command.InstrumentIds ?? []]);
        if (scope.IsEmpty && await autoScope.LatestAsync(cancellationToken) is { } auto)
        {
            scope = new ReplayScope(auto.BasketIds, auto.InstrumentIds);
        }

        if (scope.IsEmpty)
        {
            return Fail("scope_empty", "no scope was given and no Auto scope is stored");
        }

        var profile = await ResolveProfileAsync(command.RiskProfileId, cancellationToken);
        if (profile is null)
        {
            return Fail("bad_rule", "no such risk profile");
        }

        var capital = command.Capital ?? ReplayRequestResolver.DefaultCapital;
        var parameters = new ReplayParams(target.Start, target.End, scope, capital, profile, sheet.Value.Id, ReplayVersionRule.Start, ReplayMode.Frozen);
        if (parameters.Problems() is { Count: > 0 } problems)
        {
            return Fail("bad_rule", string.Join("; ", problems));
        }

        var members = (await facts.UniverseAsync(parameters, IndiaTime.Today(now), cancellationToken)).Members.Select(m => m.Id).Union(scope.InstrumentIds).Distinct().Order().ToList();
        if (members.Count == 0)
        {
            return Fail("scope_empty", "the scope resolves to no instrument");
        }

        if (members.Count * replayOptions.BytesPerInstrument > replayOptions.MaxWindowMb * 1024L * 1024)
        {
            return Fail("scope_over_memory_cap", $"the scope reads {members.Count} instruments; the largest scope allowed is {replayOptions.MaxWindowMb * 1024L * 1024 / replayOptions.BytesPerInstrument} (Replay:MaxWindowMb)");
        }

        var settings = await facts.SettingsAsync(cancellationToken);
        var versions = (await inputs.VersionsAsync(parameters, cancellationToken)).ToDictionary(p => p.Key, p => p.Value.ToList(), StringComparer.Ordinal);
        var config = new HoldOutRegistrationConfig(
            target.Start, target.End, parameters, ["selection", "baseline"], sheet.Value.RatesSha256, settings, ReplayJson.Sha256(ReplayJson.Canonical(settings)),
            ReplayHashes.Scope(scope, members), members.Count, versions, engine.EngineVersion, rule);
        var text = ReplayJson.Canonical(config);
        var row = new StoredHoldOutRegistration(Guid.CreateVersion7(now), target.Start, text, ReplayJson.Sha256(text), command.Mode, command.Actor, now);
        await store.InsertAsync(row, cancellationToken);
        return Result.Success(new RegisteredHoldOut(row.Id, row.ConfigHash, row.BlockStart));
    }

    private async ValueTask<Result<CostSheet>> ResolveSheetAsync(RegisterHoldOutCommand command, CancellationToken cancellationToken)
    {
        if (command.Mode == AutoMode && command.CostSheetId is null)
        {
            return await sheets.ListCheckedAsync(cancellationToken) is { Count: > 0 } checkedSheets
                ? Result.Success(checkedSheets[0])
                : Result.Failure<CostSheet>(HoldOutRegistrationErrors.Invalid("sheet_unchecked", "no charge sheet is checked (ISS-25), so no net figure can be read"));
        }

        if (string.IsNullOrWhiteSpace(command.CostSheetId) || string.Equals(command.CostSheetId, "gross", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<CostSheet>(HoldOutRegistrationErrors.Invalid("gross_choice", "a gross run never counts; name a checked charge sheet"));
        }

        var found = await sheets.FindAsync(command.CostSheetId, cancellationToken);
        if (found.IsFailure)
        {
            return Result.Failure<CostSheet>(HoldOutRegistrationErrors.Invalid("sheet_missing", $"the charge sheet `{command.CostSheetId}` is not stored"));
        }

        return found.Value.Checked is null
            ? Result.Failure<CostSheet>(HoldOutRegistrationErrors.Invalid("sheet_unchecked", $"the charge sheet `{command.CostSheetId}` is not checked (ISS-25)"))
            : found;
    }

    private async ValueTask<ReplayRiskProfile?> ResolveProfileAsync(Guid? id, CancellationToken cancellationToken)
    {
        if (id is { } given)
        {
            return await profiles.FindAsync(given, cancellationToken) is { } found ? From(found) : null;
        }

        return (await profiles.ListAsync(cancellationToken)).FirstOrDefault(p => p.IsSeeded && string.Equals(p.Name, "Standard", StringComparison.OrdinalIgnoreCase)) is { } standard ? From(standard) : ReplayRiskProfile.Standard;
    }

    private static ReplayRiskProfile From(XDrishti.Domain.Accounts.RiskProfile p) =>
        new((double)p.RiskPerTradePct, (double)p.MaxCapitalPerTradePct, (double)p.MaxTotalRiskPct, p.MaxPerSector, (double)p.DailyLossPct);

    private static Result<RegisteredHoldOut> Fail(string reason, string message) => Result.Failure<RegisteredHoldOut>(HoldOutRegistrationErrors.Invalid(reason, message));
}
