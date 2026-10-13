namespace XDrishti.Api.Endpoints;

/// <summary>
/// The multipart form of the trade preview and import, for the API contract only (the request is read by <see cref="TradeUploads"/>). <c>AccountId</c> is the account, <c>File</c> the tradebook (CSV), <c>Rebuild</c> and <c>Digest</c> are
/// for the import: whether to rebuild the lots (default true) and the digest of the preview, which is required with a rebuild.
/// </summary>
internal sealed record TradeUploadForm(Guid AccountId, IFormFile File, bool? Rebuild, string? Digest);
