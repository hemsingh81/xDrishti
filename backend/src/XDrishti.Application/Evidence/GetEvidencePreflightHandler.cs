using System.Globalization;
using XDrishti.Application.Abstractions.Evidence;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.AutoSwitch;
using XDrishti.Application.ChargeSheet;
using XDrishti.Application.Lab;
using XDrishti.Application.Market.Context;
using XDrishti.Application.Market.History;
using XDrishti.Application.Paper;
using XDrishti.Application.Platform.SystemStatus;
using XDrishti.Application.Replay;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.History;

namespace XDrishti.Application.Evidence;

/// <summary>
/// The preflight of the first-evidence runbook (spec EV-01): composes reads that exist and applies the state rules. A read that throws, or a fact that is missing, is <c>UNAVAILABLE</c> with the exception type only (no message, so no secret): never <c>MET</c>.
/// Nothing is written and nothing leaves the host.
/// </summary>
internal sealed class GetEvidencePreflightHandler(
    IQueryHandler<GetSystemStatusQuery, SystemStatusResponse> status,
    IEvidencePreflightHost host,
    IQueryHandler<GetChargeSheetStatusQuery, Result<ChargeSheetStatusView>> chargeSheet,
    IQueryHandler<GetContextCoverageQuery, ContextCoverageView> context,
    IQueryHandler<GetHoldOutQuery, Result<HoldOutView>> holdOut,
    ICellStatsStore cells,
    ITradingCalendar calendar,
    IModelRegistry models,
    IQueryHandler<GetShadowSummaryQuery, ShadowSummaryView> shadow,
    IServiceScheduleReader schedules,
    IQueryHandler<GetAutoQuery, AutoView> auto,
    IQueryHandler<ShowHistoryQuery, IReadOnlyList<HistoryRow>> history,
    IBackfillSettings backfill,
    ISimAllBackfillGate backfillGate,
    SystemReplayOptions replay,
    TimeProvider clock) : IQueryHandler<GetEvidencePreflightQuery, EvidencePreflightView>
{
    public const string Met = "MET";
    public const string NotMet = "NOT MET";
    public const string Unavailable = "UNAVAILABLE";

    /// <summary>The runbook's guideline (until LC-01), not a selection rule.</summary>
    public const int MaxCellSnapshotSessions = 5;

    public const string DeployedVersion = "deployed version";
    public const string ChargeSheetItem = "charge sheet confirmed at current rates";
    public const string ContextItem = "context probe (index, VIX, regime)";
    public const string HoldOutItem = "hold-out fixed";
    public const string CellsItem = "newest cell snapshot age";
    public const string ChampionItem = "champion designated";
    public const string ShadowItem = "shadow switch";
    public const string CaptureItem = "paper-capture service";
    public const string AutoItem = "Auto switch and chain";
    public const string BackfillItem = "backfill status";
    public const string MemoryItem = "replay memory headroom";
    public const string HistorySwitchesItem = "NSE history and admit-verified switches";

    public async ValueTask<EvidencePreflightView> HandleAsync(GetEvidencePreflightQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var ct = cancellationToken;
        return new EvidencePreflightView(
        [
            await GuardAsync(DeployedVersion, "run deploy/xd-up.sh", () => VersionAsync(ct), ct),
            await GuardAsync(ChargeSheetItem, "load a sheet, check it against your Dhan reports, then Confirm on Settings > Advanced > Charge sheet (runbook step 3)", () => ChargeSheetAsync(ct), ct),
            await GuardAsync(ContextItem, "track NIFTY 50, INDIA VIX and Nifty 500, update the data, then scripts/context.sh coverage (runbook step 4)", () => ContextAsync(ct), ct),
            await GuardAsync(HoldOutItem, "it is fixed at the first Lab post: load history first, then run a Lab test (runbook steps 5-7)", () => HoldOutAsync(ct), ct),
            await GuardAsync(CellsItem, "xd learn cells --as-of YYYY-MM-DD --run RUNKEY:STYLE:EXIT (runbook step 8)", () => CellsAsync(ct), ct),
            await GuardAsync(ChampionItem, "xd learn designate --model N --role champion --reason TEXT (runbook step 9)", () => ChampionAsync(ct), ct),
            await GuardAsync(ShadowItem, "owner act: PUT /api/paper/shadow/settings with a fresh password (runbook step 11.1)", () => ShadowAsync(ct), ct),
            await GuardAsync(CaptureItem, "resume paper-capture on the Services screen (runbook step 11.2)", () => CaptureAsync(ct), ct),
            await GuardAsync(AutoItem, "Auto screen: set the scope, then Auto ON with a fresh password (runbook step 11.3)", () => AutoAsync(ct), ct),
            await GuardAsync(BackfillItem, "xd data backfill, then xd data probe-seam (runbook step 6.3)", () => BackfillAsync(ct), ct),
            await GuardAsync(MemoryItem, "raise Replay:MaxWindowMb only together with the xd-api mem_limit in deploy/compose.yaml (ADR 0272)", () => ValueTask.FromResult(Memory()), ct),
            await GuardAsync(HistorySwitchesItem, "set Data:History:Nse:Enabled and Data:History:Replay:AdmitVerified for xd-api (runbook step 6)", () => ValueTask.FromResult(Switches()), ct),
        ]);
    }

    private static async ValueTask<EvidencePreflightItem> GuardAsync(string item, string fix, Func<ValueTask<(bool? Met, string Reason)>> read, CancellationToken ct)
    {
        try
        {
            var (met, reason) = await read();
            return new EvidencePreflightItem(item, met is null ? Unavailable : met.Value ? Met : NotMet, reason, met == true ? "none" : fix);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return new EvidencePreflightItem(item, Unavailable, "the read failed: " + failure.GetType().Name, fix);
        }
    }

    private async ValueTask<(bool? Met, string Reason)> VersionAsync(CancellationToken ct)
    {
        var db = (await status.HandleAsync(new GetSystemStatusQuery(), ct)).Database;
        var compiled = host.NewestCompiledMigration();
        if (!db.IsConnected || compiled is null)
        {
            return (null, db.IsConnected ? "the build lists no migration" : "the database is not reachable");
        }

        var met = db.PendingMigrations == 0 && db.LatestMigration == compiled;
        return (met, $"applied {db.LatestMigration ?? "none"}, this build {compiled}, pending {db.PendingMigrations} (the image tag proves nothing)");
    }

    private async ValueTask<(bool? Met, string Reason)> ChargeSheetAsync(CancellationToken ct)
    {
        var result = await chargeSheet.HandleAsync(new GetChargeSheetStatusQuery(null), ct);
        if (result.IsFailure)
        {
            return (null, "the status was refused: " + result.Error.Code);
        }

        var state = result.Value.State;
        return (state == "confirmed", state == "stale" ? "stale: the confirmation carries another rates hash than the stored sheet" : "state " + state);
    }

    private async ValueTask<(bool? Met, string Reason)> ContextAsync(CancellationToken ct)
    {
        var c = await context.HandleAsync(new GetContextCoverageQuery(), ct);
        if (!c.Enabled || c.Missing is not null)
        {
            return (false, c.Missing ?? "the context is switched off");
        }

        var met = c.IndexTrendReadableFrom is not null && c.RegimeReadableFrom is not null && c.VixPctReadableFrom is not null;
        return (met, $"index trend from {Date(c.IndexTrendReadableFrom)}, regime from {Date(c.RegimeReadableFrom)}, VIX percentile from {Date(c.VixPctReadableFrom)}");
    }

    private async ValueTask<(bool? Met, string Reason)> HoldOutAsync(CancellationToken ct)
    {
        var result = await holdOut.HandleAsync(new GetHoldOutQuery(), ct);
        if (result.IsFailure)
        {
            return (null, "the read was refused: " + result.Error.Code);
        }

        return result.Value.Block is { } block
            ? (true, $"block from {Date(block.Start)} to {Date(block.HistoryLast)}")
            : (false, "not fixed: it is fixed at the first Lab post (ADR 0207)");
    }

    private async ValueTask<(bool? Met, string Reason)> CellsAsync(CancellationToken ct)
    {
        if (await cells.NewestAsOfAsync(ct) is not { } asOf)
        {
            return (false, "no cell snapshot is stored");
        }

        var days = await calendar.GetAsync(ct);
        var age = days.TradingDaysBetween(asOf, days.LastClosedSession(clock.GetUtcNow()));
        return (age <= MaxCellSnapshotSessions, $"newest as of {Date(asOf)}, {age} sessions old (the runbook asks for at most {MaxCellSnapshotSessions}; guidance until LC-01)");
    }

    private async ValueTask<(bool? Met, string Reason)> ChampionAsync(CancellationToken ct)
    {
        var champions = await models.ListAsync(ModelRole.Champion, 1, ct);
        return (champions.Count > 0, champions.Count > 0 ? "model " + champions[0].Id.ToString(CultureInfo.InvariantCulture) + " is the champion" : "no model is designated champion");
    }

    private async ValueTask<(bool? Met, string Reason)> ShadowAsync(CancellationToken ct)
    {
        var s = await shadow.HandleAsync(new GetShadowSummaryQuery(), ct);
        return (s.Enabled, s.Enabled ? "paper.shadow.enabled is ON" : "paper.shadow.enabled is OFF (the owner switched it off; the default is ON, ADR 0282)");
    }

    private async ValueTask<(bool? Met, string Reason)> CaptureAsync(CancellationToken ct)
    {
        var info = await schedules.GetAsync(PaperCaptureService.Id, ct);
        return info is null ? (null, "the service is not registered") : (info.Enabled, info.Enabled ? "on: " + info.Words : "paused (it starts paused)");
    }

    private async ValueTask<(bool? Met, string Reason)> AutoAsync(CancellationToken ct)
    {
        var a = await auto.HandleAsync(new GetAutoQuery(), ct);
        var reason = !a.On ? "Auto is OFF (the default)" : a.Complete ? "Auto is ON and the chain is complete" : "Auto is ON but the chain is not complete: " + (a.Note ?? "a service is paused");
        return (a.On && a.Complete, reason);
    }

    private async ValueTask<(bool? Met, string Reason)> BackfillAsync(CancellationToken ct)
    {
        var rows = await history.HandleAsync(new ShowHistoryQuery(), ct);
        if (rows.Count == 0)
        {
            return (false, "no tracked instrument");
        }

        var failed = rows.Count(r => r.ChunksFailed > 0);
        var seams = rows.Count(r => r.Status is BackfillStatus.SeamUnresolved or BackfillStatus.SeamJump);
        var pending = rows.Count(r => r.Status is BackfillStatus.None or BackfillStatus.Planned or BackfillStatus.Partial && r.ChunksFailed == 0);
        var verified = rows.Count(r => r.Verified == ShowHistoryHandler.Yes);
        var counts = $"{rows.Count} instruments: {verified} verified, {pending} pending, {failed} failed chunks, {seams} with an unresolved seam or a seam jump (information only)";
        var incomplete = await backfillGate.IncompleteReasonAsync(ct);
        return (incomplete is null, (incomplete is null ? "complete (a replay may start); " : "a replay is refused: " + incomplete + " ") + counts);
    }

    private (bool? Met, string Reason) Memory()
    {
        var limit = host.ComposeApiMemLimitMib();
        if (limit is null)
        {
            return (null, "the xd-api mem_limit could not be read from deploy/compose.yaml");
        }

        var ceiling = limit.Value * 3 / 4 - 256;
        var path = host.ComposePath() ?? "deploy/compose.yaml";
        var largest = replay.MaxWindowMb * 1024L * 1024 / replay.BytesPerInstrument;
        return (replay.MaxWindowMb <= ceiling, $"Replay:MaxWindowMb {replay.MaxWindowMb} MiB against {ceiling} MiB (0.75 x mem_limit {limit} MiB - 256 MiB, mem_limit from {path}; Replay:MaxWindowMb from this host's configuration, xd-api may differ); largest scope {largest} instruments");
    }

    private (bool? Met, string Reason) Switches()
    {
        var nse = host.NseHistoryEnabled();
        var admit = backfill.AdmitVerified;
        return (nse && admit, $"Data:History:Nse:Enabled {(nse ? "on" : "off")}, Data:History:Replay:AdmitVerified {(admit ? "on" : "off")} (read from this host's configuration; xd-api may differ)");
    }

    private static string Date(DateOnly? date) => date is { } d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "-";
}
