using Microsoft.Extensions.Configuration;
using XDrishti.Application.Abstractions.Planning;

namespace XDrishti.Infrastructure.Planning;

/// <summary>Reads <c>Planning:OpenBook:AssumedEmpty</c> (the owner setting <c>planning.open_book_assumed_empty</c>, P7-09, ADR 0092). Only the exact text `true` turns it on (`True`, `TRUE`, ` true`, `1` read false): the open book is unavailable.</summary>
internal sealed class OpenBookSettings(IConfiguration configuration) : IOpenBookSettings
{
    public bool AssumedEmpty => string.Equals(configuration["Planning:OpenBook:AssumedEmpty"], "true", StringComparison.Ordinal);
}
