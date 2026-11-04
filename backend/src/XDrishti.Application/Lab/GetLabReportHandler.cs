using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>Builds the report of a succeeded full or robust run from the stored run and result (spec P5-06). Reads only; no engine pass.</summary>
internal sealed class GetLabReportHandler(
    ILabRunStore runs,
    ILabResultStore results,
    LabSettings settings,
    IRegistrationStore? registrations = null,
    IHoldOutStore? holdOuts = null,
    RegistrationOptions? registrationOptions = null,
    HoldOutOptions? holdOutOptions = null) : IQueryHandler<GetLabReportQuery, Result<LabReportView>>
{
    public async ValueTask<Result<LabReportView>> HandleAsync(GetLabReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var run = await runs.GetAsync(query.Id, cancellationToken);
        if (run is null)
        {
            return Result.Failure<LabReportView>(LabErrors.RunNotFound(query.Id));
        }

        var state = LabRunStates.Code(run.State);
        if (run.State != LabRunState.Succeeded || !LabSettings.RunsFullTest(run.Method))
        {
            return Result.Failure<LabReportView>(LabErrors.NoReport(state, run.Method));
        }

        var verdict = await results.GetAsync(run.Id, cancellationToken);
        if (verdict is null)
        {
            return Result.Failure<LabReportView>(LabErrors.NoReport(state, run.Method));
        }

        var report = LabReportBuilder.Build(LabRunView.Of(run, verdict), verdict, settings.MinFoldsForPass);
        return Result.Success(await WithConfidenceAsync(report, run, cancellationToken));
    }

    /// <summary>CF-06: the registration and hold-out rows are the state now (a registration or an unlock after the run changes them); the hold-out numbers appear only in a look that was taken.</summary>
    private async ValueTask<LabReportView> WithConfidenceAsync(LabReportView report, LabRun run, CancellationToken cancellationToken)
    {
        if (registrations is null || holdOuts is null)
        {
            return report;
        }

        var enforceRegistration = registrationOptions?.Enforce ?? false;
        var stored = await registrations.GetAsync(run.StrategyId, run.Version, cancellationToken);
        var registration = stored is null
            ? new ReportRegistrationView("not_registered", enforceRegistration, null, "No pre-registration was written for this version: the row reads fail" + (enforceRegistration ? "." : " and nothing is refused until the owner turns enforcement on."))
            : new ReportRegistrationView("registered", enforceRegistration, RegisterStrategyHandler.ViewOf(stored), "Registered before the data was searched; the text is frozen.");
        var block = await holdOuts.CurrentAsync(cancellationToken);
        var unlock = await holdOuts.GetUnlockAsync(run.StrategyId, run.Version, HoldOutUnlocker.StrategyKind, cancellationToken);
        var reaches = block is not null && run.To >= block.Start;
        var enforceHoldOut = holdOutOptions?.Enforce ?? false;
        var holdOut = unlock is not null
            ? new ReportHoldOutView(unlock.Result.State, enforceHoldOut, block?.Start, block?.LengthMonths, reaches, unlock.Result.State == "reused" ? "Hold-out: looked at again; the numbers are shown and cannot count toward a pass." : "Hold-out: used once.", unlock.Result)
            : block is not null
                ? new ReportHoldOutView("kept", enforceHoldOut, block.Start, block.LengthMonths, reaches, reaches ? "Hold-out: kept, not yet used. This run's window reached it, so it cannot count as unseen." : "Hold-out: kept, not yet used.", null)
                : new ReportHoldOutView("not_fixed", enforceHoldOut, null, null, false, "Hold-out: no block is fixed yet: none was set since the hold-out was introduced, or the stored history is too short.", null);
        var warnings = new List<ReportWarningView>(report.Warnings);
        if (stored is null)
        {
            warnings.Add(new ReportWarningView("not_registered", "This version has no pre-registration (who loses, expected signs, scope, expected edge, falsifier)."));
        }

        if (holdOut.State == "not_fixed" || (reaches && unlock is null))
        {
            warnings.Add(new ReportWarningView("holdout_not_kept", holdOut.Text));
        }

        return report with { Registration = registration, HoldOut = holdOut, Warnings = warnings };
    }
}
