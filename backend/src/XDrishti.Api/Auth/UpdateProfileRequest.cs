namespace XDrishti.Api.Auth;

/// <summary>Body of <c>PUT /api/auth/profile</c>.</summary>
internal sealed record UpdateProfileRequest(string DisplayName, string Email);
