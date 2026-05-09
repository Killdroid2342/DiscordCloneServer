using System.Security.Cryptography;
using System.Text;
using DiscordCloneServer.Data;
using DiscordCloneServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiscordCloneServer.Services
{
    public sealed record InviteAbuseDetectionRequest(
        string ServerId,
        string InviteId,
        string InviteCode,
        string JoinedUsername,
        string? IpAddress);

    public sealed record InviteAbuseDetectionResult(
        bool Allowed,
        string ReasonCode,
        string Message,
        int RetryAfterSeconds,
        int RecentInviteUses,
        int RecentIpUses,
        bool ShouldRevokeInvite = false)
    {
        public static InviteAbuseDetectionResult Allow(int recentInviteUses = 0, int recentIpUses = 0) =>
            new(true, "allowed", string.Empty, 0, recentInviteUses, recentIpUses);
    }

    public interface IInviteAbuseDetectionService
    {
        Task<InviteAbuseDetectionResult> CheckAndTrackAsync(
            InviteAbuseDetectionRequest request,
            CancellationToken cancellationToken = default);
    }


}
