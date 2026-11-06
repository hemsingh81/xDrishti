using System.Globalization;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Paper;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Readiness;

namespace XDrishti.Application.Confidence;

/// <summary>
/// Reads the forward evidence of one strategy for the scorecard (spec CF-11 D2 and D3) through the existing producers: the edge series (CF-08, CF-01), the decay report (the point-in-time reference of P10-04, the drift alarm of P6-09, CF-12), the sequential failure test (CF-09),
/// the kill proposals (CF-12), the P10 phase check and the champion model's calibration (CF-10). A producer that cannot be read is an input that does not exist (unavailable, never a pass). Read only.
/// </summary>
internal sealed class ForwardEvidenceReader(
    ForwardEdgeReader edge,
    IQueryHandler<GetDecayReportQuery, Result<DecayReportView>> decay,
    IQueryHandler<GetSequentialStatusQuery, Result<SequentialStatusView>> sequential,
    IQueryHandler<GetKillProposalsQuery, Result<KillProposalsView>> kills,
    IQueryHandler<EvaluatePaperPhaseCheckQuery, Result<PaperPhaseCheckView>> phase,
    IModelRegistry models,
    StrategyCells cells)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async ValueTask<ForwardEvidence> ReadAsync(string strategy, CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        var forward = await edge.ReadAsync(strategy, cancellationToken);
        var report = await decay.HandleAsync(new GetDecayReportQuery(strategy), cancellationToken);
        var failure = await sequential.HandleAsync(new GetSequentialStatusQuery(GetSequentialStatusQuery.StrategyScope, strategy), cancellationToken);
        notes.Add(string.Create(Inv, $"The forward series is the graded shadow edge candidates of the current paper epoch ({forward.Trades} trades, effective n {forward.NEff:0.#}); {forward.Basis}. The same-day correlation is the placeholder 0.3 until CF-01 estimates it."));
        if (forward.Gross)
        {
            notes.Add("The forward series is gross of charges (the cost sheet is not confirmed), so its mean, spread and months are not offered as net: the effect, agreement and paper-confirm rows are unavailable.");
        }

        var (model, ece) = await CalibrationAsync(cancellationToken);
        var score = new ScoreInputs
        {
            ForwardNEff = forward.NEff,
            ForwardMeanR = forward.MeanR,
            ForwardSdR = forward.SdR,
            PositiveMonthShare = forward.PositiveMonthShare,
            Band = BandOf(report, forward, notes),
            ModelApplies = model,
            EcePp = ece,
            DriftAlarm = report.IsSuccess ? report.Value.Edge.Cusum?.Alarm : null,
        };
        var met = await PhaseAsync(cancellationToken);
        return new ForwardEvidence(score, forward.MeanR, forward.StandardError, met, FailureBoundaryOf(failure), await KillDemotionAsync(strategy, cancellationToken), notes);
    }

    /// <summary>
    /// Where the forward mean sits against the 5th percentile of the point-in-time backtest reference of P10-04: <c>mu_ref - 1.645 x sigma x sqrt(1/n_eff + 1/N_ref)</c>. The reference is the decay report's (the strategy's backtest trades before the epoch started); it is read only when it
    /// is on the same net basis as the series.
    /// </summary>
    private static BandPosition? BandOf(Result<DecayReportView> report, ForwardEdge forward, List<string> notes)
    {
        if (report.IsFailure || report.Value.Reference is not { State: "available", MeanR: { } mu, Sigma: { } sigma, Trades: { } n, Basis: "net" })
        {
            notes.Add("Backtest agreement: no net point-in-time backtest reference of this strategy is available, so the forward mean is not compared with a band.");
            return null;
        }

        if (forward.MeanR is not { } mean || forward.NEff <= 0 || n <= 0)
        {
            return null;
        }

        var lower = mu - (ScoreAnchors.LowerBoundZ * sigma * Math.Sqrt((1 / forward.NEff) + (1.0 / n)));
        notes.Add(string.Create(Inv, $"Backtest agreement: the band's 5th percentile is read as the reference mean {mu:0.###} R less 1.645 x sigma x sqrt(1/n_eff + 1/N_ref) = {lower:0.###} R; the reference includes in-sample trades."));
        return mean < lower ? BandPosition.BelowBand : BandPosition.InsideBand;
    }

    private static FaultState FailureBoundaryOf(Result<SequentialStatusView> status) =>
        status.IsFailure ? FaultState.Unavailable : status.Value.Edge.FirstFailureTrade is not null || status.Value.Edge.State == "failure" ? FaultState.Fault : FaultState.Clear;

    private async ValueTask<(bool Applies, double? Ece)> CalibrationAsync(CancellationToken cancellationToken)
    {
        var champions = await models.ListAsync(ModelRole.Champion, 1, cancellationToken);
        return champions.Count == 0 ? (false, null) : (true, champions[0].Model.PooledEcePp);
    }

    private bool? _phase;

    private bool _phaseRead;

    /// <summary>The P10 phase check is the system's, not a strategy's, so it is read once per instance (a scope) however many strategies ask.</summary>
    private async ValueTask<bool?> PhaseAsync(CancellationToken cancellationToken)
    {
        if (_phaseRead)
        {
            return _phase;
        }

        var result = await phase.HandleAsync(new EvaluatePaperPhaseCheckQuery(null, null), cancellationToken);
        _phase = result.IsFailure ? null : result.Value.Report.ExitVerdict switch
        {
            "MET" => true,
            "NOT MET" => false,
            _ => null,
        };
        _phaseRead = true;
        return _phase;
    }

    private async ValueTask<FaultState> KillDemotionAsync(string strategy, CancellationToken cancellationToken)
    {
        var applied = await kills.HandleAsync(new GetKillProposalsQuery("applied", strategy, 200), cancellationToken);
        if (applied.IsFailure)
        {
            return FaultState.Unavailable;
        }

        foreach (var cell in applied.Value.Items.SelectMany(p => p.Cells).Distinct(StringComparer.Ordinal))
        {
            if (await cells.StateAsync(cell, cancellationToken) == CellState.Paused)
            {
                return FaultState.Fault;
            }
        }

        return FaultState.Clear;
    }
}
