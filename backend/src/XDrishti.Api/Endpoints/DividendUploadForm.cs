using XDrishti.Application.Portfolio.DividendImport;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The multipart form of the dividend preview and import, for the API contract only (the request is read by <see cref="DividendUploads"/>). <c>AccountId</c> is the account, <c>File</c> the dividend list (CSV). For the import also <c>Digest</c> (required: the
/// digest of the preview), <c>Choices</c> and <c>IncludeDuplicates</c>. A form field is text: <b>send <c>Choices</c> and <c>IncludeDuplicates</c> as JSON text</b> (<c>JSON.stringify</c> of a <c>DividendChoice[]</c> and of a list of line numbers of possible
/// duplicates to import anyway); the types here say what the JSON holds.
/// </summary>
internal sealed record DividendUploadForm(Guid AccountId, IFormFile File, string? Digest, IReadOnlyList<DividendChoice>? Choices, IReadOnlyList<int>? IncludeDuplicates);
