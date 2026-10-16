using System.IO.Compression;
using System.Text;
using XDrishti.Domain.Common;
using XDrishti.Infrastructure.Portfolio;

namespace XDrishti.Infrastructure.Tests.Portfolio;

/// <summary>
/// The spreadsheet reader (ExcelDataReader, ADR 0029, P2-17 criterion 3) on small synthetic workbooks - a legacy BIFF8 <c>.xls</c> and an <c>.xlsx</c> with the same invented rows - and on files that are not workbooks. No file here is a real download.
/// </summary>
public sealed class SpreadsheetReaderTests
{
    private static Stream Sample(string name) =>
        typeof(SpreadsheetReaderTests).Assembly.GetManifestResourceStream("Spreadsheets/" + name) ?? throw new InvalidOperationException($"Sample {name} is not embedded.");

    private static async Task<IReadOnlyList<IReadOnlyList<string>>> ReadAsync(string sample)
    {
        await using var stream = Sample(sample);
        var result = await new SpreadsheetReader().ReadAsync(stream, 1000, CancellationToken.None);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    [Theory]
    [InlineData("dp-charges-synthetic.xls")]
    [InlineData("dp-charges-synthetic.xlsx")]
    public async Task The_rows_of_the_first_sheet_come_back_as_text_cells_with_numbers_and_dates_written_as_the_port_says(string sample)
    {
        var rows = await ReadAsync(sample);

        rows.Count.ShouldBe(9);
        rows[0][0].ShouldBe("DP Charges | From 01-April-2026 to 15-October-2026");
        rows[2].Select(c => c.Trim()).ShouldBe(["Sr.", "Date", "ISIN", "Security Name", "", "", "", "Quantity", "Buy/Sell", "Type of Transaction", "", "Charges"]);
        rows[3][0].ShouldBe("1");
        rows[3][1].ShouldBe("04-Jun-2026");
        rows[3][2].ShouldBe("INE245A01021");
        rows[3][3].ShouldBe("  THE EXAMPLE POWER COMPANY LIMITED");
        rows[3][7].ShouldBe("11");
        rows[3][8].ShouldBe(" Sell");
        rows[3][11].ShouldBe("12.5");
        rows[4][1].ShouldBe("2026-06-05");
        rows[4][11].ShouldBe("0");
        rows[6][0].ShouldBe("Total");
        rows[8][0].ShouldBe("Generated on 15-10-2026");
    }

    [Fact]
    public async Task The_two_formats_give_the_same_rows()
    {
        var legacy = await ReadAsync("dp-charges-synthetic.xls");
        var modern = await ReadAsync("dp-charges-synthetic.xlsx");

        legacy.Select(r => string.Join('|', r.Take(12))).ShouldBe(modern.Select(r => string.Join('|', r.Take(12))));
    }

    [Fact]
    public async Task A_workbook_with_more_rows_than_the_limit_is_refused_on_the_file_field()
    {
        await using var stream = Sample("dp-charges-synthetic.xlsx");

        var result = await new SpreadsheetReader().ReadAsync(stream, 3, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields!.Single().Field.ShouldBe("file");
        result.Error.Fields!.Single().Message.ShouldContain("more than 3 rows");
    }

    [Theory]
    [InlineData("text")]
    [InlineData("random")]
    [InlineData("truncated-xls")]
    [InlineData("empty-zip")]
    [InlineData("zip-without-workbook")]
    public async Task A_file_that_is_not_a_readable_workbook_is_refused_without_an_exception(string kind)
    {
        var bytes = kind switch
        {
            "text" => Encoding.UTF8.GetBytes("Date,ISIN\n2026-06-04,INE245A01021\n"),
            "random" => [.. Enumerable.Range(0, 600).Select(i => (byte)((i * 31) % 251))],
            "truncated-xls" => Truncated("dp-charges-synthetic.xls", 700),
            "empty-zip" => EmptyZip(),
            _ => ZipWith("notes.txt", "hello"),
        };
        await using var stream = new MemoryStream(bytes);

        var result = await new SpreadsheetReader().ReadAsync(stream, 1000, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("portfolio.invalid");
        result.Error.Fields!.Single().Field.ShouldBe("file");
    }

    private static string Sheet(string dimension, params string[] rows) =>
        $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><dimension ref="{dimension}"/><sheetData>{string.Concat(rows)}</sheetData></worksheet>""";

    private static string Row(int number, params (string Reference, string Text)[] cells) =>
        $"""<row r="{number}">{string.Concat(cells.Select(c => $"""<c r="{c.Reference}" t="inlineStr"><is><t>{c.Text}</t></is></c>"""))}</row>""";

    /// <summary>A minimal .xlsx package around one sheet's XML.</summary>
    private static byte[] Workbook(string sheetXml)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>""");
            Add(archive, "_rels/.rels", """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Add(archive, "xl/workbook.xml", """<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            Add(archive, "xl/_rels/workbook.xml.rels", """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""");
            Add(archive, "xl/worksheets/sheet1.xml", sheetXml);
        }

        return buffer.ToArray();
    }

    private static void Add(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static async Task<Result<IReadOnlyList<IReadOnlyList<string>>>> ReadBytesAsync(byte[] bytes)
    {
        await using var stream = new MemoryStream(bytes);
        return await new SpreadsheetReader().ReadAsync(stream, 1000, CancellationToken.None);
    }

    [Fact]
    public async Task A_hand_made_minimal_workbook_is_read()
    {
        var result = await ReadBytesAsync(Workbook(Sheet("A1:B1", Row(1, ("A1", "Date"), ("B1", "ISIN")))));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Single().ShouldBe(["Date", "ISIN"]);
    }

    [Theory]
    [InlineData("<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\"><v>1</v></c></row>")]
    [InlineData("not xml at all <<<")]
    [InlineData("<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>x</is></c></row></sheetData></worksheet>")]
    public async Task A_workbook_whose_xml_is_broken_is_refused_on_the_file_field_without_an_exception(string sheetXml)
    {
        var result = await ReadBytesAsync(Workbook(sheetXml));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("portfolio.invalid");
        result.Error.Fields!.Single().Field.ShouldBe("file");
    }

    [Fact]
    public async Task A_sheet_that_declares_a_huge_dimension_is_refused_before_it_is_held()
    {
        var result = await ReadBytesAsync(Workbook(Sheet("A1:XFD1048576", Row(1, ("A1", "Date"), ("XFD1", "far")))));

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields!.Single().Field.ShouldBe("file");
        result.Error.Fields!.Single().Message.ShouldContain("too large");
    }

    [Fact]
    public async Task A_sheet_with_more_cells_than_the_limit_is_refused()
    {
        var rows = Enumerable.Range(1, 5).Select(r => Row(r, ("A" + r, "a"), ("B" + r, "b"), ("C" + r, "c"), ("D" + r, "d"))).ToArray();
        await using var stream = new MemoryStream(Workbook(Sheet("A1:D5", rows)));

        var result = await new SpreadsheetReader { CellLimit = 16 }.ReadAsync(stream, 1000, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields!.Single().Message.ShouldContain("too large");
    }

    [Fact]
    public async Task Reading_can_be_cancelled()
    {
        await using var stream = Sample("dp-charges-synthetic.xlsx");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await new SpreadsheetReader().ReadAsync(stream, 1000, cancelled.Token));
    }

    private static byte[] Truncated(string sample, int length)
    {
        using var stream = Sample(sample);
        var all = new byte[stream.Length];
        stream.ReadExactly(all);
        return all[..length];
    }

    private static byte[] EmptyZip()
    {
        using var buffer = new MemoryStream();
        using (new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
        }

        return buffer.ToArray();
    }

    private static byte[] ZipWith(string name, string content)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }

        return buffer.ToArray();
    }
}
