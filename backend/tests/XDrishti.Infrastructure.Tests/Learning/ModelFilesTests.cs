using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using XDrishti.Application.Learning;
using XDrishti.Infrastructure.Learning;

namespace XDrishti.Infrastructure.Tests.Learning;

/// <summary>The model files (spec P6-07, ADR 0070): named by hash, written atomically, never read back without the bytes still hashing to their name. Uses a temporary folder of its own.</summary>
public sealed class ModelFilesTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "xd-model-files-" + Guid.NewGuid().ToString("N"));

    private ModelFiles Files() => new(Options.Create(new ModelStoreOptions { Folder = _folder }));

    private static (byte[] Bytes, string Hash) Model(byte seed)
    {
        var bytes = new byte[] { seed, 2, 3, 4, 5 };
        return (bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task A_written_file_is_read_back_and_leaves_no_temporary_file()
    {
        var (bytes, hash) = Model(1);

        var written = await Files().WriteAsync(hash, bytes, Ct);
        var read = await Files().ReadAsync(hash, Ct);

        written.IsSuccess.ShouldBeTrue();
        read.Value.ShouldBe(bytes);
        Directory.GetFiles(_folder).Select(Path.GetFileName).ShouldBe([hash]);
    }

    [Fact]
    public async Task A_stale_temporary_file_of_the_hash_is_cleaned_at_write_and_the_files_are_owner_only()
    {
        var (bytes, hash) = Model(1);
        Directory.CreateDirectory(_folder);
        var stale = Path.Combine(_folder, hash + ".0123.tmp");
        var other = Path.Combine(_folder, "other.tmp");
        await File.WriteAllBytesAsync(stale, [1], Ct);
        await File.WriteAllBytesAsync(other, [1], Ct);

        await Files().WriteAsync(hash, bytes, Ct);

        File.Exists(stale).ShouldBeFalse();
        File.Exists(other).ShouldBeTrue();
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(Path.Combine(_folder, hash)).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task Writing_the_same_file_twice_leaves_it_untouched()
    {
        var (bytes, hash) = Model(1);
        await Files().WriteAsync(hash, bytes, Ct);
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(_folder, hash), stamp);

        var again = await Files().WriteAsync(hash, bytes, Ct);

        again.IsSuccess.ShouldBeTrue();
        File.GetLastWriteTimeUtc(Path.Combine(_folder, hash)).ShouldBe(stamp);
    }

    [Fact]
    public async Task Bytes_that_do_not_hash_to_the_name_are_refused_and_nothing_is_written()
    {
        var (_, hash) = Model(1);

        var result = await Files().WriteAsync(hash, [9, 9, 9], Ct);

        result.Error.Code.ShouldBe(ModelErrors.FileCorruptCode);
        Directory.Exists(_folder).ShouldBeFalse();
    }

    [Fact]
    public async Task A_file_changed_on_disk_is_corrupt_and_a_missing_one_is_missing()
    {
        var (bytes, hash) = Model(1);
        await Files().WriteAsync(hash, bytes, Ct);
        await File.WriteAllBytesAsync(Path.Combine(_folder, hash), [1, 1, 1], Ct);

        var corrupt = await Files().ReadAsync(hash, Ct);
        var missing = await Files().ReadAsync(Model(2).Hash, Ct);

        corrupt.Error.Code.ShouldBe(ModelErrors.FileCorruptCode);
        missing.Error.Code.ShouldBe(ModelErrors.FileMissingCode);
    }

    [Fact]
    public async Task A_corrupt_file_is_replaced_by_the_right_bytes_on_the_next_write()
    {
        var (bytes, hash) = Model(1);
        await Files().WriteAsync(hash, bytes, Ct);
        await File.WriteAllBytesAsync(Path.Combine(_folder, hash), [1, 1, 1], Ct);

        var written = await Files().WriteAsync(hash, bytes, Ct);

        written.IsSuccess.ShouldBeTrue();
        (await Files().ReadAsync(hash, Ct)).Value.ShouldBe(bytes);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    [InlineData("abc")]
    public async Task A_name_that_is_not_a_lower_case_hash_never_becomes_a_path(string name)
    {
        var read = await Files().ReadAsync(name, Ct);
        var write = await Files().WriteAsync(name, [1], Ct);

        read.Error.Code.ShouldBe(ModelErrors.FileCorruptCode);
        write.Error.Code.ShouldBe(ModelErrors.FileCorruptCode);
        Directory.Exists(_folder).ShouldBeFalse();
    }
}
