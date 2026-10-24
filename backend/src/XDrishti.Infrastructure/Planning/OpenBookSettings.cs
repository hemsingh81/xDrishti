using Microsoft.Extensions.Configuration;
using XDrishti.Application.Abstractions.Planning;

namespace XDrishti.Infrastructure.Planning;

/// <summary>Reads <c>Planning:OpenBook:AssumedEmpty</c> (the owner setting <c>planning.open_book_assumed_empty</c>, P7-09, ADR 0092). Missing or not a boolean means false: the open book is unavailable.</summary>
internal sealed class OpenBookSettings(IConfiguration configuration) : IOpenBookSettings
{
    public bool AssumedEmpty => bool.TryParse(configuration["Planning:OpenBook:AssumedEmpty"], out var on) && on;
}
