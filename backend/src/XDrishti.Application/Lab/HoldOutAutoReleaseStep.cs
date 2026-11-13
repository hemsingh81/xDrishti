namespace XDrishti.Application.Lab;

/// <summary>The auto release of the seal service (HO-01 D4, D10): the last step, so a look the earlier steps queued keeps the block this evening. Releases only through <see cref="HoldOutReleaser.TryAutoAsync"/>.</summary>
internal sealed class HoldOutAutoReleaseStep(HoldOutReleaser releaser) : IHoldOutSealStep
{
    public string Name => "auto release";

    public int Order => 90;

    public async ValueTask<string> RunAsync(CancellationToken cancellationToken)
    {
        var result = await releaser.TryAutoAsync(cancellationToken);
        return result.Released ? "block released (auto)" : "kept: " + result.Reason;
    }
}
