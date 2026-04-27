using System.Security.Claims;

namespace DiscordCloneServer.Services
{
    public static class AuthClaims
    {
        public const string UsernameClaim = "username";
        public const string SessionIdClaim = "sid";

        public static string? GetUsername(this ClaimsPrincipal user)
        {
            return user.FindFirstValue(UsernameClaim) ?? user.FindFirstValue(ClaimTypes.Name);
        }

        public static string? GetSessionId(this ClaimsPrincipal user)
        {
            return user.FindFirstValue(SessionIdClaim);
        }
    }
}
