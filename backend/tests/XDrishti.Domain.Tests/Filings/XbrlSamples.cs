using System.Globalization;
using System.Text;
using System.Xml;
using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>Hand-made XBRL instance documents of a fake company (spec P15-03 criterion 2). The element names are the starting list of <see cref="ResultsTaxonomyMap"/>, not a verified taxonomy: these files are synthetic.</summary>
internal static class XbrlSamples
{
    public const string Inr = "u-inr";
    public const string PerShare = "u-eps";
    public const string Usd = "u-usd";

    public static readonly DateOnly Q1 = new(2026, 6, 30);

    public static string Document(string body, string? basis = "Consolidated", string? rounding = "Actual", string rootNamespaces = "")
    {
        var markers = new StringBuilder();
        if (basis is not null)
        {
            markers.Append(CultureInfo.InvariantCulture, $"<in:NatureOfReportStandaloneConsolidated contextRef=\"c-q\">{basis}</in:NatureOfReportStandaloneConsolidated>");
        }

        if (rounding is not null)
        {
            markers.Append(CultureInfo.InvariantCulture, $"<in:LevelOfRoundingUsedInFinancialStatements contextRef=\"c-q\">{rounding}</in:LevelOfRoundingUsedInFinancialStatements>");
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <xbrli:xbrl xmlns:xbrli="http://www.xbrl.org/2003/instance" xmlns:in="http://synthetic.example/taxonomy/in-results" xmlns:iso4217="http://www.xbrl.org/2003/iso4217" xmlns:xbrldi="http://xbrl.org/2006/xbrldi" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:link="http://www.xbrl.org/2003/linkbase" xmlns:xlink="http://www.w3.org/1999/xlink"{rootNamespaces}>
              <link:schemaRef xlink:type="simple" xlink:href="https://synthetic.example/taxonomy/in-results.xsd" />
              <xbrli:unit id="{Inr}"><xbrli:measure>iso4217:INR</xbrli:measure></xbrli:unit>
              <xbrli:unit id="{Usd}"><xbrli:measure>iso4217:USD</xbrli:measure></xbrli:unit>
              <xbrli:unit id="{PerShare}"><xbrli:divide><xbrli:unitNumerator><xbrli:measure>iso4217:INR</xbrli:measure></xbrli:unitNumerator><xbrli:unitDenominator><xbrli:measure>xbrli:shares</xbrli:measure></xbrli:unitDenominator></xbrli:divide></xbrli:unit>
              {markers}
              {body}
            </xbrli:xbrl>
            """;
    }

    public static string Duration(string id, string start, string end, string? member = null, string? segmentDimension = null)
    {
        var dims = new StringBuilder();
        if (member is not null)
        {
            dims.Append(CultureInfo.InvariantCulture, $"<xbrldi:explicitMember dimension=\"in:ConsolidatedAndStandaloneAxis\">in:{member}</xbrldi:explicitMember>");
        }

        if (segmentDimension is not null)
        {
            dims.Append(CultureInfo.InvariantCulture, $"<xbrldi:explicitMember dimension=\"in:{segmentDimension}\">in:SegmentOneMember</xbrldi:explicitMember>");
        }

        var segment = dims.Length > 0 ? $"<xbrli:segment>{dims}</xbrli:segment>" : string.Empty;
        return $"<xbrli:context id=\"{id}\"><xbrli:entity><xbrli:identifier scheme=\"http://synthetic.example\">FAKE</xbrli:identifier>{segment}</xbrli:entity><xbrli:period><xbrli:startDate>{start}</xbrli:startDate><xbrli:endDate>{end}</xbrli:endDate></xbrli:period></xbrli:context>";
    }

    public static string Instant(string id, string date) =>
        $"<xbrli:context id=\"{id}\"><xbrli:entity><xbrli:identifier scheme=\"http://synthetic.example\">FAKE</xbrli:identifier></xbrli:entity><xbrli:period><xbrli:instant>{date}</xbrli:instant></xbrli:period></xbrli:context>";

    public static string Num(string element, string context, string unit, string value) => $"<in:{element} contextRef=\"{context}\" unitRef=\"{unit}\" decimals=\"0\">{value}</in:{element}>";

    public static string Word(string element, string context, string value) => $"<in:{element} contextRef=\"{context}\">{value}</in:{element}>";

    /// <summary>A full set of seven numbers and the audited word for one context.</summary>
    public static string FullSet(string context, string revenue = "1000.50", string income = "1100.75", string pbt = "200", string pat = "150", string basic = "2.50", string diluted = "2.45", string exceptional = "-5", string audited = "Unaudited") =>
        Num("RevenueFromOperations", context, Inr, revenue) + Num("Income", context, Inr, income) + Num("ProfitBeforeTax", context, Inr, pbt) + Num("ProfitOrLossAttributableToOwnersOfParent", context, Inr, pat)
        + Num("BasicEarningsLossPerShareFromContinuingAndDiscontinuedOperations", context, PerShare, basic) + Num("DilutedEarningsLossPerShareFromContinuingAndDiscontinuedOperations", context, PerShare, diluted)
        + Num("ExceptionalItemsBeforeTax", context, Inr, exceptional) + Word("WhetherResultsAreAuditedOrUnaudited", context, audited);

    public static ParsedResults Parse(string xml, DateOnly? periodEnd = null)
    {
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return ResultsXbrlParser.Parse(reader, periodEnd ?? Q1);
    }

    public static string QuarterDocument(string? basis = "Consolidated") => Document(Duration("c-q", "2026-04-01", "2026-06-30") + FullSet("c-q"), basis);
}
