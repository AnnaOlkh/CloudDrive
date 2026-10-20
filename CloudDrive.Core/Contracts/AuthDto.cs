namespace CloudDrive.Core.Contracts;

public record RegisterRequest(string UserName, string Password);
public record LoginRequest(string UserName, string Password);
public record LoginResponse(string Token, string UserName);
public record ApiError(string Error);
