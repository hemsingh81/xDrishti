using System.Security.Cryptography;
using XDrishti.Api.Simulate;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>Criterion 12 of P3-04b1 (the sink half): the sink hashes and counts as it writes, with one bounded buffer, and gives the same SHA-256 and length as the finished file.</summary>
public sealed class HashingFileSinkTests
{
    private static (long Bytes, string Sha256, byte[] OnDisk) Write(int bufferSize, Action<HashingFileSink> render)
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "file.bin");
        (long, string) done;
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        using (var sink = new HashingFileSink(stream, bufferSize))
        {
            render(sink);
            done = sink.Complete();
        }

        return (done.Item1, done.Item2, File.ReadAllBytes(path));
    }

    private static void Append(HashingFileSink sink, byte[] data)
    {
        data.CopyTo(sink.GetSpan(data.Length));
        sink.Advance(data.Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    [InlineData(65_536)]
    public void The_hash_and_the_count_are_those_of_the_finished_file(int bufferSize)
    {
        var data = new byte[200_003];
        new Random(7).NextBytes(data);

        var (bytes, sha256, onDisk) = Write(bufferSize, sink =>
        {
            for (var offset = 0; offset < data.Length; offset += 1000)
            {
                Append(sink, data[offset..Math.Min(offset + 1000, data.Length)]);
            }
        });

        bytes.ShouldBe(data.Length);
        onDisk.ShouldBe(data);
        sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(onDisk)));
    }

    [Fact]
    public void A_request_larger_than_the_buffer_gets_a_larger_one()
    {
        var data = new byte[5000];
        new Random(8).NextBytes(data);

        var (bytes, sha256, onDisk) = Write(16, sink => Append(sink, data));

        bytes.ShouldBe(5000);
        onDisk.ShouldBe(data);
        sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(data)));
    }

    [Fact]
    public void An_empty_file_has_the_hash_of_nothing()
    {
        var (bytes, sha256, onDisk) = Write(16, _ => { });

        bytes.ShouldBe(0);
        onDisk.ShouldBeEmpty();
        sha256.ShouldBe("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public void The_memory_flavour_and_the_span_flavour_write_the_same_bytes()
    {
        var (_, _, onDisk) = Write(8, sink =>
        {
            sink.GetMemory(3).Span[..3].Fill((byte)'a');
            sink.Advance(3);
            sink.GetSpan(3)[..3].Fill((byte)'b');
            sink.Advance(3);
        });

        onDisk.ShouldBe("aaabbb"u8.ToArray());
    }

    [Fact]
    public void A_buffer_is_never_larger_than_the_hint_needs_so_memory_is_flat()
    {
        using var temp = new TempFolder();
        using var stream = new FileStream(Path.Combine(temp.Path, "f"), FileMode.CreateNew, FileAccess.Write);
        using var sink = new HashingFileSink(stream);
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 100_000; i++)
        {
            "0123456789"u8.CopyTo(sink.GetSpan(10));
            sink.Advance(10);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        sink.Complete().Bytes.ShouldBe(1_000_000);
        allocated.ShouldBeLessThan(10_000, "one megabyte went through one 64 KiB buffer");
    }

    [Fact]
    public void Misuse_is_refused()
    {
        using var temp = new TempFolder();
        using var stream = new FileStream(Path.Combine(temp.Path, "f"), FileMode.CreateNew, FileAccess.Write);
        using var sink = new HashingFileSink(stream, 16);

        Should.Throw<ArgumentOutOfRangeException>(() => sink.Advance(-1));
        Should.Throw<ArgumentOutOfRangeException>(() => sink.Advance(17));
        Should.Throw<ArgumentOutOfRangeException>(() => sink.GetSpan(-1));
        sink.Complete();
        Should.Throw<ObjectDisposedException>(() => sink.GetSpan(1));
        Should.Throw<ObjectDisposedException>(() => sink.Complete());
        Should.Throw<ArgumentNullException>(() => new HashingFileSink(null!));
        Should.Throw<ArgumentOutOfRangeException>(() => new HashingFileSink(stream, 0));
    }
}
