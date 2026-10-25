using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>"What works, in which basket, on which timeframe, with which exit" (P8-06): the cells are the dimensions of the lens, or the default cell when the lens is empty.</summary>
/// <param name="Query">The checked query.</param>
public sealed record GetReportWhatWorksQuery(ReportQuery Query) : IQuery<Result<WhatWorksView>>;
