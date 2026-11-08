using System.Globalization;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Application.Shadow;

namespace XDrishti.Api.Cli;

/// <summary><c>xd paper shadow verify|summary</c> (spec CF-08 part B, ADR 0212): recompute the digest chain of the shadow record, or print its summary. Both are read only; nothing is written and no order exists.</summary>
internal static class PaperShadowCommand
{
    public const string Usage = """
        usage:
          xd paper shadow verify
            recomputes every row hash and every session digest of the shadow record from the stored rows and names the first session that does not match; prints the last digest (keep a copy of it outside the database: it is what makes a rewrite of the whole chain visible)
          xd paper shadow summary
            raw n and effective n (with the correlation used and whether it is the placeholder), mean R with a 90% interval and the hit rate by group, the behavioural count of P10 beside the edge count, the pace, the chain head
            every figure is modelled (no queue, no latency); the R is labelled gross unless the charge sheet is confirmed as yours; nothing is written
        exit codes: 0 done (verify: the chain is intact), 4 verify: the chain is broken, 2 wrong command line, 3 failure, 130 cancelled

        """;

    public static bool Handles(IReadOnlyList<string> args) => args.Count >= 1 && args[0] == "shadow";

    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, CancellationToken cancellationToken)
    {
        if (args.Count != 2 || args[0] != "shadow" || args[1] is not ("verify" or "summary"))
        {
            await console.Error.WriteAsync(Usage);
            return XdCommand.UsageError;
        }

        try
        {
            await using var scope = services.CreateAsyncScope();
            if (args[1] == "verify")
            {
                var check = await scope.ServiceProvider.GetRequiredService<IQueryHandler<VerifyShadowChainQuery, ShadowChainCheckView>>().HandleAsync(new VerifyShadowChainQuery(), cancellationToken);
                await console.Out.WriteLineAsync(VerifyLine(check));
                return check.Intact && check.LogProblem is null ? XdCommand.Done : XdCommand.NotPassing;
            }

            var view = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetShadowSummaryQuery, ShadowSummaryView>>().HandleAsync(new GetShadowSummaryQuery(), cancellationToken);
            foreach (var line in Lines(view))
            {
                await console.Out.WriteLineAsync(SimulateOptions.Show(line));
            }

            return XdCommand.Done;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await console.Error.WriteLineAsync("cancelled");
            return XdCommand.Cancelled;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            await console.Error.WriteLineAsync("failed: " + SimulateCommand.Describe(failure));
            return XdCommand.Failed;
        }
    }

    internal static string VerifyLine(ShadowChainCheckView c) =>
        (c.LogProblem is null ? string.Empty : "digest log DIVERGED: " + c.LogProblem + " ") + VerifyChainLine(c);

    private static string VerifyChainLine(ShadowChainCheckView c) =>
        c.Intact
            ? string.Create(CultureInfo.InvariantCulture, $"shadow chain intact: {c.Sessions} session(s), {c.Rows} row(s); last session {c.LastSession?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "none"}, last digest {c.LastDigest ?? "none"}")
            : string.Create(CultureInfo.InvariantCulture, $"shadow chain BROKEN from session {c.FirstBroken:yyyy-MM-dd}: a row or a digest from that session on does not recompute from the stored rows; {c.Sessions} session(s), {c.Rows} row(s)");

    internal static IEnumerable<string> Lines(ShadowSummaryView v)
    {
        yield return "shadow record: " + (v.Enabled ? "switch ON" : "switch OFF (paper.shadow.enabled)") + "; " + v.Statement;
        yield return v.SeparateCount;
        if (v.Note is not null)
        {
            yield return v.Note;
        }

        var c = v.Counts;
        yield return string.Create(CultureInfo.InvariantCulture, $"counts: behavioural (P10, armed primary) {c.Behavioural} | edge (shadow, graded) {c.EdgeRaw}, effective {c.EdgeEffective:0.0}; candidates {c.Candidates} ({c.EdgeCandidates} count for edge, {c.Pending} pending, {c.NotTriggered} not triggered, {c.NotGradable} not gradable)");
        yield return string.Create(CultureInfo.InvariantCulture, $"effective n: raw {v.Edge.RawN}, effective {v.Edge.EffectiveN:0.0}, rho {v.Edge.Rho:0.00}{(v.Edge.RhoNote is null ? string.Empty : " (" + v.Edge.RhoNote + ")")}, {v.Edge.Sessions} session(s)");
        yield return "pace: " + v.Edge.EtaNote;
        yield return "basis: " + v.Costs.Basis;
        foreach (var g in v.Groups)
        {
            yield return $"{g.Group}: n {g.N}, mean R {Show(g.MeanR)}, 90% interval {Show(g.Low90)} to {Show(g.High90)}, hit rate {Show(g.HitRate)}";
        }

        yield return string.Create(CultureInfo.InvariantCulture, $"chain: {v.Chain.Sessions} session(s), {v.Chain.Gaps} gap(s); last {v.Chain.LastSession?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "none"}, digest {v.Chain.LastDigest ?? "none"}");
        yield return v.Sequential.Line ?? "sequential failure test: " + v.Sequential.Note;
    }

    private static string Show(MetricView m) =>
        m.Number is { } n
            ? n.ToString("0.000", CultureInfo.InvariantCulture) + (m.State == "provisional" ? " (provisional)" : string.Empty)
            : m.State + (m.Reason is null ? string.Empty : " (" + m.Reason + ")");
}
