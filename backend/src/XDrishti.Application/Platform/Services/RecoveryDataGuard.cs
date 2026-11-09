using System.Globalization;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>The guard over the ledger and the proofs (see <see cref="IRecoveryDataGuard"/>). Without a partial newest item it reads one ledger row and nothing else, so it is cheap on every ordinary day.</summary>
internal sealed class RecoveryDataGuard(IRecoveryLedger ledger, IEnumerable<IRecoveryProof> proofs) : IRecoveryDataGuard
{
    public async ValueTask<string?> WhyUnprovenAsync(DateOnly? from, CancellationToken cancellationToken)
    {
        // a later, succeeded item covers the older ones: only the newest decides
        if (await ledger.FindNewestAsync(EodService.Id, cancellationToken) is not { State: RecoveryStates.Partial } newest)
        {
            return null;
        }

        var detail = RecoveryProofDetail.Read(newest.Detail);
        if (proofs.FirstOrDefault(p => string.Equals(p.ServiceId, EodService.Id, StringComparison.Ordinal)) is { } proof)
        {
            try
            {
                if (await RecoveryProofDetail.ReproveAsync(proof, newest, cancellationToken) is { } fresh)
                {
                    detail = RecoveryProofDetail.Read(fresh.Detail);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // fail closed: what cannot be read again is not proved
            catch (Exception)
#pragma warning restore CA1031
            {
                return "the end-of-day data could not be proved complete (a store read failed); it is checked again later";
            }
        }

        if (detail is null)
        {
            return "the end-of-day data was left partial and its proof cannot be read";
        }

        var missing = detail.Sessions.Where(s => s.State == SessionProofStates.StillMissing && (from is null || s.Date >= from)).Select(s => s.Date).ToList();
        return missing.Count == 0
            ? null
            : "the end-of-day data is not complete for " + string.Join(", ", missing.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))) + "; it is waited for and checked again at the next pass";
    }
}
