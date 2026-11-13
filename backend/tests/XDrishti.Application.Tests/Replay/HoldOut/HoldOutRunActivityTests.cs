using XDrishti.Application.Replay.HoldOut;

namespace XDrishti.Application.Tests.Replay.HoldOut;

/// <summary>SR-03: the one gate between the queued replay job and the hold-out loop is atomic: whoever takes it first excludes the other until it lets go.</summary>
public sealed class HoldOutRunActivityTests
{
    [Fact]
    public void The_hold_out_loop_and_the_replay_job_exclude_each_other_until_the_holder_lets_go()
    {
        var gate = new HoldOutRunActivity();

        gate.TryEnterHoldOut().ShouldBeTrue();
        gate.Running.ShouldBeTrue();
        gate.TryEnterReplay().ShouldBeFalse();
        gate.TryEnterHoldOut().ShouldBeFalse();
        gate.ExitReplay(); // a side that does not hold it cannot release it
        gate.Running.ShouldBeTrue();
        gate.ExitHoldOut();
        gate.TryEnterReplay().ShouldBeTrue();
        gate.TryEnterHoldOut().ShouldBeFalse();
        gate.ExitHoldOut();
        gate.TryEnterHoldOut().ShouldBeFalse();
        gate.ExitReplay();
        gate.TryEnterHoldOut().ShouldBeTrue();
    }
}
