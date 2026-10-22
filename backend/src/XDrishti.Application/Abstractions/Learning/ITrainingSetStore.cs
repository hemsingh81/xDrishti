namespace XDrishti.Application.Abstractions.Learning;

/// <summary>Where training-set records are kept (P6-04, ADR 0066): <c>research.training_sets</c> behind a port. Insert-only; the id is the idempotency key.</summary>
public interface ITrainingSetStore
{
    /// <summary>Writes the record; false (nothing written) when the id is stored already.</summary>
    ValueTask<bool> StoreAsync(TrainingSetToStore record, CancellationToken cancellationToken);
}
