using XDrishti.Application.Portfolio.Attention;

namespace XDrishti.Application.Tests.Portfolio.Heal;

/// <summary>An attention reconciler that only counts its calls: for the tests of the code that has to call it (the recorder, the healer, the connect) without working out any item.</summary>
internal sealed class RecordingAttentionReconciler : IAttentionReconciler
{
    public int Reconciles { get; private set; }

    public int Retentions { get; private set; }

    /// <summary>Called at every reconcile, for a test that wants to know what had happened by then (the order of the look).</summary>
    public Action? OnReconcile { get; set; }

    /// <summary>What a reconcile waits for, with the token it was given (a test that simulates a reconcile that does not end).</summary>
    public Func<CancellationToken, Task>? Wait { get; set; }

    public async ValueTask ReconcileAsync(CancellationToken cancellationToken)
    {
        Reconciles++;
        OnReconcile?.Invoke();
        if (Wait is { } wait)
        {
            await wait(cancellationToken);
        }
    }

    public ValueTask ApplyRetentionAsync(CancellationToken cancellationToken)
    {
        Retentions++;
        return ValueTask.CompletedTask;
    }
}
