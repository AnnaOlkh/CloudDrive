using System.Security.Claims;

namespace CloudDrive.Server.Auth;

public static class ClaimsExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? throw new InvalidOperationException("The token does not contain a user identifier."));
}
