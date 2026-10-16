using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Application.Portfolio.Trades;

namespace XDrishti.Api.Endpoints;

/// <summary>A dividend file upload as read from the multipart request: the account, the file's name and bytes and, for an import, the preview's digest, the owner's choices and the possible duplicates (by line) the owner ticked.</summary>
internal sealed record DividendUpload(Guid AccountId, string FileName, TradeFileContent Content, string? Digest, IReadOnlyList<DividendChoice> Choices, IReadOnlyList<int> IncludeDuplicates);
