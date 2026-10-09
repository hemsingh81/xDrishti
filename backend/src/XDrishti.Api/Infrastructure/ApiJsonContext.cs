using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Application.Platform.SystemStatus;

namespace XDrishti.Api.Infrastructure;

/// <summary>
/// Source-generated JSON metadata: no runtime reflection for serialisation (faster, less memory).
/// Add every response/request type returned by an endpoint here.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, NumberHandling = JsonNumberHandling.Strict)]
[JsonSerializable(typeof(SystemStatusResponse))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
