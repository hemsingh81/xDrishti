using System.Buffers.Binary;
using System.Text;
using XDrishti.Infrastructure.Portfolio;

namespace XDrishti.Infrastructure.Tests.Portfolio;

/// <summary>
/// The decompression-bomb guard for <c>.xlsx</c> packages (ADR 0030): small zips that <b>declare</b> huge entries - made here byte by byte, nothing is ever unpacked - are refused with a plain 400-style failure on the file field before the XML is parsed; a real workbook and
/// a package within the limits pass.
/// </summary>
public sealed class SpreadsheetPackageGuardTests
{
    /// <summary>A zip whose central directory declares the given sizes (compressed bytes are real but stored: the guard never opens an entry).</summary>
    private static byte[] Forged(params (string Name, long Declared, int Compressed)[] entries)
    {
        using var buffer = new MemoryStream();
        var offsets = new List<int>();
        foreach (var (name, declared, compressed) in entries)
        {
            offsets.Add((int)buffer.Position);
            var nameBytes = Encoding.ASCII.GetBytes(name);
            var header = new byte[30];
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(), 0x04034b50);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 20);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(18), (uint)compressed);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(22), (uint)declared);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(26), (ushort)nameBytes.Length);
            buffer.Write(header);
            buffer.Write(nameBytes);
            buffer.Write(new byte[compressed]);
        }

        var directory = (int)buffer.Position;
        for (var i = 0; i < entries.Length; i++)
        {
            var (name, declared, compressed) = entries[i];
            var nameBytes = Encoding.ASCII.GetBytes(name);
            var header = new byte[46];
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(), 0x02014b50);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 20);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), 20);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), (uint)compressed);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), (uint)declared);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(28), (ushort)nameBytes.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(42), (uint)offsets[i]);
            buffer.Write(header);
            buffer.Write(nameBytes);
        }

        var size = (int)buffer.Position - directory;
        var end = new byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(), 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(8), (ushort)entries.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(end.AsSpan(10), (ushort)entries.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(12), (uint)size);
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(16), (uint)directory);
        buffer.Write(end);
        return buffer.ToArray();
    }

    private static string? Problem(byte[] zip)
    {
        using var stream = new MemoryStream(zip);
        var problem = SpreadsheetPackageGuard.Problem(stream);
        stream.Position.ShouldBe(0); // left ready for the reader
        return problem;
    }

    private const long Mb = 1024 * 1024;

    [Fact]
    public void A_package_within_the_limits_passes()
    {
        Problem(Forged(("[Content_Types].xml", 2_000, 400), ("xl/worksheets/sheet1.xml", 30 * Mb, (int)(30 * Mb / 12)))).ShouldBeNull();
    }

    [Fact]
    public void More_parts_than_a_workbook_has_are_refused()
    {
        var entries = Enumerable.Range(0, SpreadsheetPackageGuard.MaxEntries + 1).Select(i => ($"part{i}.xml", 100L, 60)).ToArray();

        Problem(Forged(entries)).ShouldBe($"more than {SpreadsheetPackageGuard.MaxEntries} parts");
        Problem(Forged(entries[..SpreadsheetPackageGuard.MaxEntries])).ShouldBeNull();
    }

    [Fact]
    public void A_package_that_declares_more_than_the_total_is_refused_even_when_no_part_is_extreme()
    {
        // three parts of 40 MB each that expand 100 times (within the ratio) are 120 MB together
        var part = (40 * Mb, (int)(40 * Mb / 100));

        Problem(Forged(("a.xml", part.Item1, part.Item2), ("b.xml", part.Item1, part.Item2), ("c.xml", part.Item1, part.Item2))).ShouldNotBeNull().ShouldContain("MB once unpacked");
    }

    [Fact]
    public void A_part_that_expands_far_more_than_the_ratio_is_refused()
    {
        Problem(Forged(("xl/worksheets/sheet1.xml", 20 * Mb, 20 * 1024))).ShouldNotBeNull().ShouldContain("unpacks to far more");
        Problem(Forged(("xl/worksheets/sheet1.xml", 20_000_000, 100_000))).ShouldBeNull(); // exactly at the ratio of 200
    }

    [Fact]
    public void A_small_part_is_not_judged_by_its_ratio()
    {
        Problem(Forged(("tiny.xml", 900 * 1024, 100))).ShouldBeNull();
    }

    [Fact]
    public async Task The_reader_refuses_such_a_package_before_parsing_with_a_plain_failure_on_the_file_field()
    {
        await using var stream = new MemoryStream(Forged(("xl/worksheets/sheet1.xml", 500 * Mb, 1024)));

        var result = await new SpreadsheetReader().ReadAsync(stream, 1000, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("portfolio.invalid");
        result.Error.Fields!.Single().Field.ShouldBe("file");
        result.Error.Fields!.Single().Message.ShouldContain("too large");
    }

    [Fact]
    public void Only_a_stream_that_starts_like_a_zip_is_a_package()
    {
        using var zip = new MemoryStream(Forged(("a.xml", 10, 10)));
        using var text = new MemoryStream(Encoding.UTF8.GetBytes("Date,ISIN"));
        using var empty = new MemoryStream();

        (SpreadsheetPackageGuard.IsPackage(zip), SpreadsheetPackageGuard.IsPackage(text), SpreadsheetPackageGuard.IsPackage(empty)).ShouldBe((true, false, false));
    }

    [Fact]
    public async Task The_synthetic_workbook_passes_the_guard()
    {
        await using var stream = typeof(SpreadsheetPackageGuardTests).Assembly.GetManifestResourceStream("Spreadsheets/dp-charges-synthetic.xlsx")!;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, TestContext.Current.CancellationToken);

        Problem(copy.ToArray()).ShouldBeNull();
    }

    [Fact]
    public async Task The_reader_refuses_a_stream_that_cannot_seek_instead_of_reading_it_without_the_guard()
    {
        await using var stream = new ForwardOnly(Forged(("xl/worksheets/sheet1.xml", 500 * Mb, 1024)));

        var result = await new SpreadsheetReader().ReadAsync(stream, 1000, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("portfolio.invalid");
        result.Error.Fields!.Single().Field.ShouldBe("file");
        stream.BytesRead.ShouldBe(0); // not even looked at
    }

    /// <summary>A stream that can only be read forwards, like a request body.</summary>
    private sealed class ForwardOnly(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }

        public override bool CanSeek => false;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = base.Read(buffer);
            BytesRead += read;
            return read;
        }
    }
}
