using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Shadow;
using XDrishti.Infrastructure.Paper;

namespace XDrishti.Infrastructure.Tests.Paper;

/// <summary>The digest log outside the database (ADR 0232): append only, one line per new head, a repeated head is not written again. Uses a temporary folder of its own.</summary>
public sealed class ShadowDigestFileTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "xd-shadow-digest-" + Guid.NewGuid().ToString("N"));

    private ShadowDigestFile Sink() => new(Options.Create(new ShadowOptions { DigestFolder = _folder }));

    private static ShadowDigestLine Line(int day, char digest) => new("2026-W45", new DateOnly(2026, 11, day), new string(digest, 64), 3, 1, new DateTimeOffset(2026, 11, 7, 6, 0, 0, TimeSpan.Zero));

    private string[] Lines() => File.ReadAllLines(Path.Combine(_folder, ShadowDigestFile.FileName));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string LogPath => Path.Combine(_folder, ShadowDigestFile.FileName);

    private static readonly string Zero = new('0', 64);

    [Fact]
    public async Task The_first_head_creates_the_folder_and_appends_one_line_chained_to_zeros()
    {
        (await Sink().AppendAsync(Line(5, 'a'), Ct)).ShouldBeTrue();

        Lines().ShouldBe([$"week=2026-W45 last_session=2026-11-05 digest={new string('a', 64)} sessions=3 gaps=1 written_at=2026-11-07T06:00:00Z prev={Zero}"]);
    }

    [Fact]
    public async Task The_same_head_is_not_written_twice_and_a_new_head_chains_to_the_hash_of_the_line_before()
    {
        var sink = Sink();
        await sink.AppendAsync(Line(5, 'a'), Ct);

        (await sink.AppendAsync(Line(5, 'a'), Ct)).ShouldBeFalse();
        (await sink.AppendAsync(Line(6, 'b'), Ct)).ShouldBeTrue();

        var lines = Lines();
        lines.Length.ShouldBe(2);
        lines[1].ShouldEndWith("prev=" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(lines[0]))));
        var read = await sink.ReadAsync(Ct);
        read.Problem.ShouldBeNull();
        read.Lines.Select(l => l.Digest).ShouldBe([new string('a', 64), new string('b', 64)]);
    }

    [Fact]
    public async Task A_missing_file_reads_as_not_existing_without_a_problem()
    {
        var read = await Sink().ReadAsync(Ct);

        (read.Exists, read.Lines.Count, read.Problem).ShouldBe((false, 0, null));
    }

    [Fact]
    public async Task An_edited_line_or_a_removed_middle_line_breaks_the_chain_on_read()
    {
        var sink = Sink();
        await sink.AppendAsync(Line(4, 'a'), Ct);
        await sink.AppendAsync(Line(5, 'b'), Ct);
        await sink.AppendAsync(Line(6, 'c'), Ct);
        var original = Lines();

        File.WriteAllLines(LogPath, [original[0], original[2]]);
        var removed = await sink.ReadAsync(Ct);
        File.WriteAllLines(LogPath, [original[0].Replace(new string('a', 64), new string('f', 64), StringComparison.Ordinal), original[1], original[2]]);
        var edited = await sink.ReadAsync(Ct);

        removed.Problem.ShouldNotBeNull().ShouldContain("Line 2");
        edited.Problem.ShouldNotBeNull().ShouldContain("Line 2");
    }

    [Fact]
    public async Task A_torn_last_line_without_its_newline_is_a_problem_and_nothing_is_appended_after_it()
    {
        var sink = Sink();
        await sink.AppendAsync(Line(5, 'a'), Ct);
        var before = await File.ReadAllTextAsync(LogPath, Ct);
        await File.WriteAllTextAsync(LogPath, before.TrimEnd('\n'), Ct);

        (await sink.ReadAsync(Ct)).Problem.ShouldNotBeNull().ShouldContain("newline");
        await Should.ThrowAsync<InvalidOperationException>(async () => await sink.AppendAsync(Line(6, 'b'), Ct));
        (await File.ReadAllTextAsync(LogPath, Ct)).ShouldBe(before.TrimEnd('\n'));
    }

    [Fact]
    public async Task A_malformed_line_is_a_problem()
    {
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(LogPath, "not a digest line\n", Ct);

        (await Sink().ReadAsync(Ct)).Problem.ShouldNotBeNull().ShouldContain("malformed");
    }

    [Fact]
    public async Task A_writable_folder_passes_and_leaves_no_probe_and_a_file_in_its_place_fails()
    {
        (await Sink().CheckWritableAsync(Ct)).ShouldBeNull();
        Directory.GetFiles(_folder, ".probe-*").ShouldBeEmpty();
        var blocked = Path.Combine(_folder, "blocked");
        await File.WriteAllTextAsync(blocked, "x", Ct);

        (await new ShadowDigestFile(Options.Create(new ShadowOptions { DigestFolder = blocked })).CheckWritableAsync(Ct)).ShouldNotBeNull();
    }
}
