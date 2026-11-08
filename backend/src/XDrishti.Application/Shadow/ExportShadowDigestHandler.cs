using System.Globalization;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Shadow;

/// <summary>
/// Appends the newest digest of the shadow chain to a log outside the database (spec CF-08 review fix 4, ADR 0232) so that a database owner cannot rewrite the chain unseen. It recomputes the chain first and refuses to export a chain that does not verify
/// (it would copy a broken head). With the switch OFF or no stored session it exports nothing. Idempotent: the same head is not written twice. It writes no database row.
/// </summary>
internal sealed class ExportShadowDigestHandler(IShadowSettings settings, IQueryHandler<VerifyShadowChainQuery, ShadowChainCheckView> verify, IShadowDigestSink sink, TimeProvider time) : ICommandHandler<ExportShadowDigestCommand, ShadowDigestExported>
{
    public const string BrokenCode = "paper.shadow_chain_broken";

    public const string DivergedCode = "paper.shadow_digest_diverged";

    public const string UnwritableCode = "paper.shadow_digest_unwritable";

    public async ValueTask<Result<ShadowDigestExported>> HandleAsync(ExportShadowDigestCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await settings.IsEnabledAsync(cancellationToken))
        {
            return Result.Success(new ShadowDigestExported(false, false, "The shadow record is switched off: nothing exported."));
        }

        var chain = await verify.HandleAsync(new VerifyShadowChainQuery(), cancellationToken);
        if (chain.FirstBroken is { } broken)
        {
            return Result.Failure<ShadowDigestExported>(Error.Conflict(BrokenCode, string.Create(CultureInfo.InvariantCulture, $"The shadow chain does not verify from the session {broken:yyyy-MM-dd}: its digest was not exported.")));
        }

        if (chain.LogProblem is { } diverged)
        {
            return Result.Failure<ShadowDigestExported>(Error.Conflict(DivergedCode, diverged + " Nothing was exported."));
        }

        if (await sink.CheckWritableAsync(cancellationToken) is { } unwritable)
        {
            return Result.Failure<ShadowDigestExported>(Error.Conflict(UnwritableCode, unwritable));
        }

        if (chain.LastSession is not { } lastSession || chain.LastDigest is not { } lastDigest)
        {
            return Result.Success(new ShadowDigestExported(false, false, "No shadow session is stored: nothing exported."));
        }

        var now = time.GetUtcNow();
        var day = IndiaTime.Today(now).ToDateTime(TimeOnly.MinValue);
        var week = string.Create(CultureInfo.InvariantCulture, $"{ISOWeek.GetYear(day)}-W{ISOWeek.GetWeekOfYear(day):00}");
        var line = new ShadowDigestLine(week, lastSession, lastDigest, chain.Sessions, chain.Gaps, now);
        var written = await sink.AppendAsync(line, cancellationToken);
        var text = string.Create(CultureInfo.InvariantCulture, $"{ShadowDigestExported.ExportedPrefix}{lastSession:yyyy-MM-dd} {(written ? "appended to" : "was already in")} the log outside the database.");
        return Result.Success(new ShadowDigestExported(true, written, text));
    }
}
