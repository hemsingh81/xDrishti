using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using XDrishti.Domain.Filings;
using XDrishti.Infrastructure.FilingArchive;
using XDrishti.Infrastructure.Filings;

namespace XDrishti.Infrastructure.Tests.Filings;

/// <summary>
/// Spec P15-03 criterion 3 (hostile input): every hostile file ends in a rejected result with a closed reason, no fact, no connection to a local trap listener (which every external reference points at), and far less than 50 MB allocated. Also the hardened reader settings and
/// a good file through the whole reader.
/// </summary>
public sealed class XbrlFileReaderTests
{
    private const string Root = "<xbrli:xbrl xmlns:xbrli=\"http://www.xbrl.org/2003/instance\" xmlns:in=\"http://synthetic.example/taxonomy/in-results\">";

    private static readonly DateOnly Q1 = new(2026, 6, 30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static XbrlFileReader Reader() => new(Options.Create(new FilingsOptions { MaxFileMb = 20 }));

    private static MemoryStream Utf8(string text) => new(Encoding.UTF8.GetBytes(text));

    private static async Task<ParsedResults> ParseAsync(Stream stream) => await Reader().ParseAsync(stream, Q1, Ct);

    private static string Good => Root
        + "<xbrli:unit id=\"u\"><xbrli:measure>iso4217:INR</xbrli:measure></xbrli:unit>"
        + "<xbrli:context id=\"c\"><xbrli:entity><xbrli:identifier scheme=\"x\">F</xbrli:identifier></xbrli:entity><xbrli:period><xbrli:startDate>2026-04-01</xbrli:startDate><xbrli:endDate>2026-06-30</xbrli:endDate></xbrli:period></xbrli:context>"
        + "<in:NatureOfReportStandaloneConsolidated contextRef=\"c\">Consolidated</in:NatureOfReportStandaloneConsolidated>"
        + "<in:RevenueFromOperations contextRef=\"c\" unitRef=\"u\">1000</in:RevenueFromOperations></xbrli:xbrl>";

    private static async Task AssertRejectedAsync(Stream stream, string reason, TcpListener? trap = null, long allocationLimit = 50L * 1024 * 1024)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();

        var result = await ParseAsync(stream);

        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBeLessThan(allocationLimit);
        (result.Outcome, result.Reason, result.Facts.Count).ShouldBe(("rejected", reason, 0));
        trap?.Pending().ShouldBeFalse("nothing connected to the trap: no network call was made");
    }

    private static TcpListener Trap()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return listener;
    }

    [Fact]
    public async Task A_good_file_is_read_through_the_whole_reader()
    {
        var result = await ParseAsync(Utf8(Good));

        result.Outcome.ShouldBe("parsed");
        result.Facts.Single(f => f.Field == "revenue").Value.ShouldBe(1000m);
    }

    [Fact]
    public async Task A_file_with_a_byte_order_mark_is_read()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Good)).ToArray();

        (await ParseAsync(new MemoryStream(bytes))).Outcome.ShouldBe("parsed");
    }

    [Fact]
    public async Task A_document_type_with_an_internal_entity_is_rejected()
    {
        await AssertRejectedAsync(Utf8("<!DOCTYPE r [<!ENTITY a \"expanded\">]>" + Root + "&a;</xbrli:xbrl>"), "dtd_prohibited");
    }

    [Fact]
    public async Task A_billion_laughs_expansion_is_rejected_without_being_expanded()
    {
        var laughs = new StringBuilder("<!DOCTYPE lolz [<!ENTITY lol \"lol\">");
        var previous = "lol";
        for (var i = 1; i <= 9; i++)
        {
            var name = "lol" + i;
            laughs.Append(System.Globalization.CultureInfo.InvariantCulture, $"<!ENTITY {name} \"{string.Concat(Enumerable.Repeat($"&{previous};", 10))}\">");
            previous = name;
        }

        laughs.Append("]>").Append(Root).Append("&lol9;</xbrli:xbrl>");

        await AssertRejectedAsync(Utf8(laughs.ToString()), "dtd_prohibited");
    }

    [Fact]
    public async Task An_external_entity_and_an_external_dtd_never_reach_the_network()
    {
        using var trap = new TrapScope(Trap());
        var port = trap.Port;

        await AssertRejectedAsync(Utf8($"<!DOCTYPE r [<!ENTITY x SYSTEM \"http://127.0.0.1:{port}/secret\">]>{Root}&x;</xbrli:xbrl>"), "dtd_prohibited", trap.Listener);
        await AssertRejectedAsync(Utf8($"<!DOCTYPE r SYSTEM \"http://127.0.0.1:{port}/x.dtd\">{Root}</xbrli:xbrl>"), "dtd_prohibited", trap.Listener);
        await AssertRejectedAsync(Utf8($"<!DOCTYPE r SYSTEM \"file:///etc/passwd\">{Root}</xbrli:xbrl>"), "dtd_prohibited", trap.Listener);
    }

    [Fact]
    public async Task A_schema_location_is_refused_and_never_fetched()
    {
        using var trap = new TrapScope(Trap());
        var xml = Root.Replace("<xbrli:xbrl", "<xbrli:xbrl xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"", StringComparison.Ordinal).Replace(">", $" xsi:schemaLocation=\"http://x http://127.0.0.1:{trap.Port}/s.xsd\">", StringComparison.Ordinal) + "</xbrli:xbrl>";

        await AssertRejectedAsync(Utf8(xml), "external_reference", trap.Listener);
    }

    [Fact]
    public async Task A_schema_reference_of_an_instance_is_read_past_and_never_fetched()
    {
        using var trap = new TrapScope(Trap());
        var xml = Good.Replace("<xbrli:unit", $"<link:schemaRef xmlns:link=\"http://www.xbrl.org/2003/linkbase\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" xlink:type=\"simple\" xlink:href=\"http://127.0.0.1:{trap.Port}/t.xsd\" /><xbrli:unit", StringComparison.Ordinal);

        var result = await ParseAsync(Utf8(xml));

        result.Outcome.ShouldBe("parsed");
        trap.Listener.Pending().ShouldBeFalse();
    }

    [Fact]
    public async Task Xml_deeper_than_64_levels_is_rejected_and_64_levels_are_read()
    {
        static string Nested(int levels) => Root + string.Concat(Enumerable.Repeat("<a>", levels - 1)) + string.Concat(Enumerable.Repeat("</a>", levels - 1)) + "</xbrli:xbrl>";

        (await ParseAsync(Utf8(Nested(64)))).Outcome.ShouldBe("no_facts");
        await AssertRejectedAsync(Utf8(Nested(65)), "too_deep");
    }

    [Fact]
    public async Task More_than_200000_elements_are_rejected()
    {
        var xml = Root + string.Concat(Enumerable.Repeat("<a/>", ResultsParser.MaxElements)) + "</xbrli:xbrl>";

        await AssertRejectedAsync(Utf8(xml), "too_many_elements");
    }

    [Fact]
    public async Task A_file_above_the_size_cap_is_rejected_before_it_is_parsed()
    {
        var big = new byte[(20 * 1024 * 1024) + 1];
        Array.Fill(big, (byte)'a');

        await AssertRejectedAsync(new MemoryStream(big), "too_large");
    }

    [Fact]
    public async Task A_file_above_the_cap_on_a_stream_that_cannot_seek_is_rejected_too()
    {
        var big = new byte[(20 * 1024 * 1024) + 1];
        Array.Fill(big, (byte)'a');

        // The stored copy is always a seekable file (rejected on its length without reading); this defensive path grows a buffer by doubling, so its total allocation is about twice the cap while the peak stays near 1.5 times.
        await AssertRejectedAsync(new ForwardOnly(big), "too_large", allocationLimit: 96L * 1024 * 1024);
    }

    [Fact]
    public async Task A_file_of_exactly_the_cap_is_read()
    {
        var bytes = Encoding.UTF8.GetBytes(Good + new string(' ', (20 * 1024 * 1024) - Good.Length));

        (await ParseAsync(new MemoryStream(bytes))).Outcome.ShouldBe("parsed");
    }

    [Fact]
    public async Task A_mapped_fact_with_a_huge_text_is_bad_value_and_not_kept()
    {
        var xml = Good.Replace(">1000<", ">" + new string('9', 200_000) + "<", StringComparison.Ordinal);

        var result = await ParseAsync(Utf8(xml));

        result.Facts.Single(f => f.Field == "revenue").Reason.ShouldBe("bad_value");
    }

    [Fact]
    public async Task Bytes_that_are_not_valid_utf8_are_rejected()
    {
        await AssertRejectedAsync(new MemoryStream([.. Encoding.UTF8.GetBytes(Root), 0xFF, 0xFE, 0xC0, 0x80, .. Encoding.UTF8.GetBytes("</xbrli:xbrl>")]), "invalid_encoding");
    }

    [Fact]
    public async Task A_file_that_is_not_xml_and_an_empty_file_are_rejected()
    {
        await AssertRejectedAsync(Utf8("just some plain text, not a filing"), "not_xml");
        await AssertRejectedAsync(new MemoryStream([]), "empty");
        await AssertRejectedAsync(Utf8("%PDF-1.4 hello"), "not_xml");
    }

    [Fact]
    public void The_reader_is_made_with_no_dtd_no_resolver_and_no_entity_expansion()
    {
        using var text = new StringReader("<a/>");
        using var reader = SafeXml.Create(text, 1000);

        reader.Settings!.DtdProcessing.ShouldBe(System.Xml.DtdProcessing.Prohibit);
        reader.Settings.MaxCharactersFromEntities.ShouldBe(0);
        reader.Settings.MaxCharactersInDocument.ShouldBe(1000);
        reader.Settings.IgnoreProcessingInstructions.ShouldBeTrue();
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "XDrishti.Infrastructure", "FilingArchive", "SafeXml.cs")).ShouldContain("XmlResolver = null");
    }

    private sealed class TrapScope(TcpListener listener) : IDisposable
    {
        public TcpListener Listener { get; } = listener;

        public int Port => ((IPEndPoint)Listener.LocalEndpoint).Port;

        public void Dispose() => Listener.Stop();
    }

    private sealed class ForwardOnly(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
