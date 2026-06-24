namespace BioAccess.Web.DTOs.Api;

public sealed record TokenResponse(string Token, int ExpiresIn);
