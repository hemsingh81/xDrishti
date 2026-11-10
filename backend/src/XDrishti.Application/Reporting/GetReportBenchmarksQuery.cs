using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The buy-and-hold of baskets and the benchmark index over one window (P8-07). All fields are the raw text of the request and are checked by the handler.</summary>
/// <param name="Basket">Basket ids, comma separated.</param>
/// <param name="From">First India date, yyyy-MM-dd; default from the default run set.</param>
/// <param name="To">Last India date, yyyy-MM-dd; default from the default run set.</param>
/// <param name="Purpose"><c>training</c> reads the window of a simulate-everything job (AU-14): the range is cut to the training window and the answer says so; default research.</param>
/// <param name="SimJob">The job (a guid); the active-else-newest job when empty. Only with <c>purpose=training</c>.</param>
/// <param name="MembersAsOf">India date of the members, yyyy-MM-dd; default the newest contributing run, else today.</param>
public sealed record GetReportBenchmarksQuery(string? Basket, string? From, string? To, string? MembersAsOf, string? Purpose = null, string? SimJob = null) : IQuery<Result<ReportBenchmarksView>>;
