using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Infrastructure.FilingArchive;
using XDrishti.Infrastructure.Filings;

namespace XDrishti.Infrastructure.Tests.Filings;

/// <summary>Spec P15-02 criteria 3 and 10: the store folder copies a stream under its hash, 0600 in 0700, re-hashes the copy, replaces nothing and leaves no half file.</summary>
public sealed class FilingFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xd-filing-files-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FilingFiles Files() => new(Options.Create(new FilingsOptions { FilesPath = Path.Combine(_root, "files") }));

    private string FilesRoot => Path.Combine(_root, "files");

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public async Task A_stream_is_copied_under_its_hash_and_the_same_bytes_again_create_nothing()
    {
        var sha = Sha("<r/>");

        var first = await Files().CopyAsync(Bytes("<r/>"), sha, 4, Ct);
        var second = await Files().CopyAsync(Bytes("<r/>"), sha, 4, Ct);

        (first.Value, second.Value).ShouldBe((true, false));
        File.ReadAllText(Path.Combine(FilesRoot, sha[..2], sha)).ShouldBe("<r/>");
        Directory.GetFiles(FilesRoot, ".tmp-*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_folders_are_created_0700_and_the_file_0600_on_posix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var sha = Sha("data");

        await Files().CopyAsync(Bytes("data"), sha, 4, Ct);

        File.GetUnixFileMode(FilesRoot).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.GetUnixFileMode(Path.Combine(FilesRoot, sha[..2])).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.GetUnixFileMode(Path.Combine(FilesRoot, sha[..2], sha)).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public async Task Bytes_that_do_not_hash_to_the_listed_hash_are_refused_and_nothing_is_left()
    {
        var sha = Sha("expected");

        var result = await Files().CopyAsync(Bytes("different"), sha, 9, Ct);

        result.Error.Code.ShouldBe("content_changed");
        Directory.GetFiles(FilesRoot, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_stream_longer_than_the_listed_size_is_refused()
    {
        var result = await Files().CopyAsync(Bytes("longer text"), Sha("longer text"), 3, Ct);

        result.Error.Code.ShouldBe("content_changed");
        Directory.GetFiles(FilesRoot, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_stream_shorter_than_the_listed_size_is_refused()
    {
        var result = await Files().CopyAsync(Bytes("abc"), Sha("abc"), 10, Ct);

        result.Error.Code.ShouldBe("content_changed");
    }

    [Fact]
    public async Task A_stream_that_fails_while_read_leaves_no_half_file_and_the_failure_surfaces()
    {
        var sha = Sha("abcdef");

        await Should.ThrowAsync<FilingContentChangedException>(async () => await Files().CopyAsync(new FailingStream(), sha, 6, Ct));

        Directory.GetFiles(FilesRoot, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_different_file_at_the_name_is_a_conflict_and_is_never_replaced_or_deleted()
    {
        var sha = Sha("real");
        var folder = Path.Combine(FilesRoot, sha[..2]);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, sha), "tampered", Ct);

        var result = await Files().CopyAsync(Bytes("real"), sha, 4, Ct);

        result.Error.Code.ShouldBe("store_conflict");
        File.ReadAllText(Path.Combine(folder, sha)).ShouldBe("tampered");
    }

    [Fact]
    public async Task Used_bytes_add_up_the_stored_files_and_a_missing_folder_is_zero()
    {
        (await Files().UsedBytesAsync(Ct)).ShouldBe(0);
        await Files().CopyAsync(Bytes("aaaa"), Sha("aaaa"), 4, Ct);
        await Files().CopyAsync(Bytes("bbbbbb"), Sha("bbbbbb"), 6, Ct);

        (await Files().UsedBytesAsync(Ct)).ShouldBe(10);
    }

    [Fact]
    public void The_free_space_of_the_volume_is_readable_even_before_the_folder_exists()
    {
        Files().FreeBytes().ShouldNotBeNull().ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData("LagMinutes", -1, false)]
    [InlineData("LagMinutes", 61, false)]
    [InlineData("LagMinutes", 0, true)]
    [InlineData("LagMinutes", 60, true)]
    [InlineData("MaxFilesGb", 0, false)]
    [InlineData("MaxFilesGb", 101, false)]
    [InlineData("MinFreeGb", 4, false)]
    [InlineData("MinFreeGb", 501, false)]
    [InlineData("MinFreeGb", 20, true)]
    public void The_limits_are_checked_at_start_up(string property, int value, bool valid)
    {
        var options = new FilingsOptions();
        typeof(FilingsOptions).GetProperty(property)!.SetValue(options, value);

        Validator.TryValidateObject(options, new ValidationContext(options), [], true).ShouldBe(valid);
    }

    [Fact]
    public void The_defaults_are_a_lag_of_five_minutes_5_gb_of_files_and_20_gb_free()
    {
        var options = new FilingsOptions();

        (options.LagMinutes, options.MaxFilesGb, options.MinFreeGb).ShouldBe((5, 5, 20));
    }

    private sealed class FailingStream : Stream
    {
        private int _reads;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_reads++ > 0)
            {
                throw new FilingContentChangedException();
            }

            "abc"u8.CopyTo(buffer.Span);
            return ValueTask.FromResult(3);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
