using System.Diagnostics;
using System.Text;
using XDrishti.Strategies.File.Tests.Support;

namespace XDrishti.Strategies.File.Tests;

/// <summary>Criterion 5 of P3-02c: hostile texts are refused with their own code, fast, with allocation bounded by the limits.</summary>
public sealed class AttackTests
{
    private const string Head = "strategy:\n  id: X\n  style: swing\n";

    private static string Laughs()
    {
        var text = new StringBuilder("a0: &l0 [x, x, x, x, x, x, x, x, x]\n");
        for (var i = 1; i < 10; i++)
        {
            text.Append('a').Append(i).Append(": &l").Append(i).Append(" [").Append(string.Join(", ", Enumerable.Repeat("*l" + (i - 1), 9))).Append("]\n");
        }

        return text.ToString();
    }

    public static TheoryData<string, string, string> Attacks => new()
    {
        { "billion laughs", Laughs(), FileCodes.AliasRefused },
        { "10 MB text", Head + "# " + new string('x', 10 * 1024 * 1024), FileCodes.TooLarge },
        { "depth 200", Head + "  trigger: " + new string('[', 200) + new string(']', 200) + "\n", FileCodes.TooDeep },
        { "many nodes", Head + "  filters: [" + string.Join(", ", Enumerable.Repeat("[" + string.Join(", ", Enumerable.Repeat("1", 90)) + "]", 60)) + "]\n", FileCodes.TooManyNodes },
        { "python tag", Head + "  filters: !!python/object/apply:os.system [ls]\n", FileCodes.TagRefused },
        { "merge key", "a: &x {b: 1}\nstrategy:\n  <<: *x\n", FileCodes.AliasRefused },
        { "merge key alone", Head.Replace("id: X", "<<: 1\n  id: X"), FileCodes.TagRefused },
        { "two documents", Head + "---\n" + Head, FileCodes.MultipleDocuments },
        { "duplicate key", Head + "  id: Y\n", FileCodes.DuplicateKey },
        { "too many keys", "strategy:\n" + string.Concat(Enumerable.Range(0, 70).Select(i => "  k" + i + ": 1\n")), FileCodes.TooManyKeys },
        { "control character", Head + "  id: \u0001\n", FileCodes.BadCharacter },
        { "complex key", "? [a, b]\n: c\n", FileCodes.BadKey },
    };

    [Theory]
    [MemberData(nameof(Attacks))]
    public void Each_attack_is_refused_with_its_own_code_in_under_100_ms_and_bounded_allocation(string name, string text, string code)
    {
        StrategyFile.Read(Example.Text);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var fastest = long.MaxValue;
        FileReadResult result = null!;
        for (var attempt = 0; attempt < 3; attempt++) // best of three: a busy machine must not fail a bound on the work itself
        {
            var clock = Stopwatch.StartNew();
            result = StrategyFile.Read(text);
            clock.Stop();
            fastest = Math.Min(fastest, clock.ElapsedMilliseconds);
        }
        var allocated = (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / 3;
        result.Definition.ShouldBeNull(name);
        result.Problems.ShouldContain(p => p.Code == code, name + ": " + string.Join(", ", result.Problems.Select(p => p.Code)));
        fastest.ShouldBeLessThan(100, name);
        allocated.ShouldBeLessThan(8 * 1024 * 1024, name);
    }

    [Fact]
    public void A_text_at_the_byte_limit_is_read_and_one_byte_more_is_refused()
    {
        var limits = new FileLimits { MaxBytes = Example.Text.Length };
        var exact = Encoding.UTF8.GetByteCount(Example.Text);

        StrategyFile.Read(Example.Text, new FileLimits { MaxBytes = exact }).HasErrors.ShouldBeFalse();
        StrategyFile.Read(Example.Text, new FileLimits { MaxBytes = exact - 1 }).Problems.ShouldContain(p => p.Code == FileCodes.TooLarge);
        limits.MaxBytes.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Depth_and_node_limits_hold_at_the_edge()
    {
        StrategyFile.Read(Head + "  filters: [a]\n", new FileLimits { MaxDepth = 3 }).HasErrors.ShouldBeFalse();
        StrategyFile.Read(Head + "  filters: [a]\n", new FileLimits { MaxDepth = 2 }).Problems.ShouldContain(p => p.Code == FileCodes.TooDeep);
        StrategyFile.Read(Head, new FileLimits { MaxNodes = 7 }).HasErrors.ShouldBeFalse();
        StrategyFile.Read(Head, new FileLimits { MaxNodes = 6 }).Problems.ShouldContain(p => p.Code == FileCodes.TooManyNodes);
    }

    [Fact]
    public void Reading_the_example_is_fast()
    {
        StrategyFile.Read(Example.Text);
        var best = double.MaxValue;
        for (var round = 0; round < 3; round++) // best of three: a busy machine must not fail a loose guard
        {
            var clock = Stopwatch.StartNew();
            for (var i = 0; i < 200; i++)
            {
                StrategyFile.Read(Example.Text);
            }

            best = Math.Min(best, clock.Elapsed.TotalMilliseconds / 200);
        }

        best.ShouldBeLessThan(5, "the target is 1 ms (criterion 10, measured by the explicit benchmark); this is a loose guard");
    }
}
