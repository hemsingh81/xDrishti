using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Reporting;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.LiveTrading;

/// <summary>The wire views of the execution feedback (spec P11-02): a figure is a state and a number only for a value; the evidence block and the stated limits are the same on every response.</summary>
internal static class LiveExecutionMapping
{
    /// <summary>The limits of the execution feedback, stated in the API and on the screen (spec P11-02 risks).</summary>
    public static readonly IReadOnlyList<string> Limits =
    [
        "Slippage is measured against the ticket's numeric level or, failing that, the plan grade's simulated entry, which already carries the modelled slippage: a positive figure means worse than the model, not worse than the market.",
        "Arrival is the open of the 1-minute bar the order was created in, not a quote; a limit order resting before the trigger has a delay near zero by construction, and for a resting limit the execution part includes the wait for the market to arrive.",
        "Reference kinds are never pooled. An entry with no reference is left out of the estimates.",
        "A partial exit, an addition to a position, or an exit on a later session is not decomposed; charges of intraday fills are not recorded, so their round trips are gross.",
        "Estimates pool all live accounts. Nothing here is applied to the simulator, the selection or a cell: the proposal sentence is read by no one automatically.",
    ];

    public static MetricView Number(double? value, string unavailableReason) =>
        value is { } v && double.IsFinite(v) ? new MetricView("value", v, null) : new MetricView("unavailable", null, unavailableReason);

    public static LiveExecutionEvidenceView Evidence(string basis, int n, DateOnly? first, DateOnly? last, BucketEdges edges) =>
        new(basis, n, first, last, ReferenceKind.All, edges.EdgesCr, Limits);

    public static LiveExecutionView Execution(StoredLiveExecution stored)
    {
        var e = stored.Row;
        const string noRef = "there is no reference level or plan grade for this entry";
        var noArrival = e.ArrivalPrice is null ? "there is no order evidence or no bar for the arrival price" : noRef;
        var noStop = e.RiskPerShare is null ? "there is no stop to measure one R" : noRef;
        var trip = e.PlanEntry is not null && e.PlanExit is not null && e.ExitVwap is not null && e.ExitQty == e.FillQty;
        return new LiveExecutionView(
            stored.Id, e.AccountId, e.ItemId, e.ForDate, e.Symbol, e.Side, e.Style, e.Product, e.Bucket, e.ReferenceKind, e.FillQty, e.FillVwap, e.ReferencePrice, e.ArrivalPrice, e.ParticipationPct, e.EntryDelaySeconds,
            Number(e.SlipTotal, noRef), e.ReferenceKind == ReferenceKind.Unavailable ? Number(null, noRef) : Number(e.SlipDelay, noArrival), e.ReferenceKind == ReferenceKind.Unavailable ? Number(null, noRef) : Number(e.SlipExec, noArrival),
            Number(e.SlipTotalTicks, e.Tick is null ? "the instrument has no tick in the master" : noRef), Number(e.SlipTotalBps, noRef), Number(e.SlipTotalR, noStop),
            e.ReferenceKind == ReferenceKind.Unavailable ? Number(null, noRef) : Number(e.SlipDelayR, e.ArrivalPrice is null ? noArrival : noStop), e.ReferenceKind == ReferenceKind.Unavailable ? Number(null, noRef) : Number(e.SlipExecR, e.ArrivalPrice is null ? noArrival : noStop),
            trip);
    }

    public static MetricView Metric(string state, double? number, string? reason, string whenMissing) =>
        state == "value" ? Number(number, whenMissing) : new MetricView(state, null, reason ?? whenMissing);
}
