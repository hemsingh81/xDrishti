namespace XDrishti.Application.Lab;

/// <summary>
/// One step of the evening seal service <c>holdout-epochs</c> (HO-01 D10). Steps run in <see cref="Order"/> and are independent: one that fails does not stop another, and a step whose read failed leaves the block as it was. The built-in steps are the strategy auto look (20) and the
/// auto release (90); CF-10e part 3 adds the model step (30) and SR-03 the system step (40), each with one DI line (the step and its <see cref="IHoldOutLookSlot"/>) and no edit of this service.
/// </summary>
internal interface IHoldOutSealStep
{
    /// <summary>The name in the run summary.</summary>
    string Name { get; }

    /// <summary>The place among the steps, lowest first.</summary>
    int Order { get; }

    /// <summary>Carries the step out and says what it did in one short phrase (never a figure of the evidence).</summary>
    ValueTask<string> RunAsync(CancellationToken cancellationToken);
}
