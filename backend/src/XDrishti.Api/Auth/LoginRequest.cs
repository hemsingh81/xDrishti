namespace XDrishti.Api.Auth;

/// <summary>Body of <c>POST /api/auth/login</c>.</summary>
internal sealed record LoginRequest(string Email, string Password);
