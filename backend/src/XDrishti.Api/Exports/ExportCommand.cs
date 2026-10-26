using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;

namespace XDrishti.Api.Exports;

/// <summary>Make one research table as a file (spec P8-20, ADR 0131). The query is the very <see cref="ReportQueryInput"/> the screen's GET takes; the page fields of it (limit, cursor) are ignored.</summary>
/// <param name="Kind">One of <see cref="ExportKinds.All"/>.</param>
/// <param name="Format">csv (the only format built).</param>
/// <param name="Query">The raw query of the screen.</param>
internal sealed record ExportCommand(string Kind, string? Format, ReportQueryInput Query) : ICommand;

/// <summary>The file of an export: its bytes (deterministic for the same stored runs and query), its server-made name and the facts the audit row and the log carry (never the content).</summary>
/// <param name="Content">The UTF-8 bytes.</param>
/// <param name="FileName">ASCII, made by the server from the kind, the India date and the table hash.</param>
/// <param name="Kind">The kind.</param>
/// <param name="Rows">The data rows.</param>
/// <param name="Filters">How many filter fields the query set (a count, never a value).</param>
/// <param name="ContentType">The media type (csv by default).</param>
/// <param name="Format">The format word of the audit row (csv, html, zip).</param>
/// <param name="Inline">True for a page shown in the browser (no attachment name).</param>
internal sealed record ExportFile(byte[] Content, string FileName, string Kind, int Rows, int Filters, string ContentType = "text/csv; charset=utf-8", string Format = "csv", bool Inline = false);

/// <summary>The tables that may be exported: research tables the Reports screens show from stored simulations, nothing about accounts (spec P8-20 assumption 1).</summary>
internal static class ExportKinds
{
    public const string Groups = "report-groups";
    public const string Pivot = "report-pivot";
    public const string WhatWorks = "report-what-works";
    public const string Equity = "report-equity";
    public const string Trades = "report-trades";

    /// <summary>The Lab's report downloads, brought under the same switch and audit (ADR 0131, decision 7 changed); not kinds of the POST.</summary>
    public const string LabReportHtml = "lab-report-html";
    public const string LabReportFolder = "lab-report-folder";

    /// <summary>The kinds built. <c>cells</c> and <c>experiments</c> of the spec are not: their views carry no evidence block, and a table without its evidence is never exported.</summary>
    public static IReadOnlyList<string> All { get; } = [Groups, Pivot, WhatWorks, Equity, Trades];
}
