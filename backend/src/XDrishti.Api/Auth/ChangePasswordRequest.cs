namespace XDrishti.Api.Auth;

/// <summary>Body of <c>POST /api/auth/change-password</c>.</summary>
internal sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
