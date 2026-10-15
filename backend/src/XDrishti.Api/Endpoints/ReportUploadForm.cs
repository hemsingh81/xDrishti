namespace XDrishti.Api.Endpoints;

/// <summary>
/// The multipart form of the report preview and import, for the API contract only (the request is read by <see cref="ReportUploads"/>). <c>AccountId</c> is the account, <c>Files</c> the downloaded reports (one or more; CSV, XLS or XLSX). <c>Choices</c> is a JSON array of
/// <c>{name, holdingId | instrumentId | isin, remember}</c>: what scrip names of a transaction report mean. For the import also <c>Digests</c> (a JSON array with the preview's digest, or null, for each file in order), <c>Rebuild</c> (whether to rebuild the lots of
/// trade files, default true) and <c>AcceptOtherAccount</c> (a JSON array of the indexes, from 0, of the files to import although the report is for another client code).
/// </summary>
internal sealed record ReportUploadForm(Guid AccountId, IFormFileCollection Files, string? Choices, string? Digests, bool? Rebuild, string? AcceptOtherAccount);
