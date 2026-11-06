using XDrishti.Application.Abstractions.Confidence;

namespace XDrishti.Application.Tests.Confidence;

/// <summary>A readiness gate for the tests of the lifecycle: by default it passes and cites nothing, so a test of a move is not changed by it; a test sets <see cref="Decision"/> to see the warning or the refusal, and reads <see cref="Requests"/> to see what was asked.</summary>
internal sealed class FakeReadinessGate : IReadinessGate
{
    public ReadinessGateDecision Decision { get; set; } = new(true, false, false, "R", null, [], "gate passed", []);

    public List<ReadinessGateRequest> Requests { get; } = [];

    public ValueTask<ReadinessGateDecision> CheckAsync(ReadinessGateRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return ValueTask.FromResult(Decision);
    }
}
