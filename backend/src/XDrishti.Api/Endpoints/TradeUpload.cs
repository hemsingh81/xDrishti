using XDrishti.Application.Portfolio.Trades;

namespace XDrishti.Api.Endpoints;

/// <summary>A tradebook upload as read from the multipart request: the account, the file's name and bytes and, for an import, whether to rebuild the lots and the preview's digest.</summary>
internal sealed record TradeUpload(Guid AccountId, string FileName, TradeFileContent Content, bool Rebuild, string? Digest);
