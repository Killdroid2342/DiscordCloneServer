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

    public sealed class InviteAbuseDetectionService : IInviteAbuseDetectionService
    {
        private readonly ApiContext _context;
        private readonly InviteAbuseDetectionOptions _options;

        public InviteAbuseDetectionService(
            ApiContext context,
            IOptions<InviteAbuseDetectionOptions> options)
        {
            _context = context;
            _options = options.Value;
            _options.Normalize();
        }

        public async Task<InviteAbuseDetectionResult> CheckAndTrackAsync(
            InviteAbuseDetectionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled)
            {
                return InviteAbuseDetectionResult.Allow();
            }

            var now = DateTime.UtcNow;
            var cutoff = now.AddMinutes(-_options.WindowMinutes);
            var ipHash = HashIpAddress(request.IpAddress);

            var recentInviteUses = await _context.ServerInviteUses
                .CountAsync(use =>
                    use.InviteId == request.InviteId &&
                    !use.WasBlocked &&
                    use.UsedAt >= cutoff,
                    cancellationToken);

            var recentIpUses = string.IsNullOrWhiteSpace(ipHash)
                ? 0
                : await _context.ServerInviteUses
                    .CountAsync(use =>
                        use.ServerId == request.ServerId &&
                        use.IpAddressHash == ipHash &&
                        !use.WasBlocked &&
                        use.UsedAt >= cutoff,
                        cancellationToken);

            var result = InviteAbuseDetectionResult.Allow(recentInviteUses, recentIpUses);
            if (_options.MaxUsesPerInviteWindow > 0 &&
                recentInviteUses + 1 > _options.MaxUsesPerInviteWindow)
            {
                result = Block(
                    "invite-join-burst",
                    "This invite is receiving joins too quickly and has been paused.",
                    recentInviteUses,
                    recentIpUses);
            }
            else if (_options.MaxUsesPerIpWindow > 0 &&
                     !string.IsNullOrWhiteSpace(ipHash) &&
                     recentIpUses + 1 > _options.MaxUsesPerIpWindow)
            {
                result = Block(
                    "invite-ip-burst",
                    "Too many accounts are joining from this network. Please wait before trying again.",
                    recentInviteUses,
                    recentIpUses);
            }

            _context.ServerInviteUses.Add(new ServerInviteUse
            {
                Id = Guid.NewGuid().ToString(),
                ServerId = request.ServerId,
                InviteId = request.InviteId,
                InviteCode = request.InviteCode,
                JoinedUsername = request.JoinedUsername,
                IpAddressHash = ipHash,
                UsedAt = now,
                WasBlocked = !result.Allowed,
                ReasonCode = result.Allowed ? null : result.ReasonCode
            });

            return result;
        }

        private InviteAbuseDetectionResult Block(
            string reasonCode,
            string message,
            int recentInviteUses,
            int recentIpUses)
        {
            return new InviteAbuseDetectionResult(
                false,
                reasonCode,
                message,
                Math.Max(60, _options.WindowMinutes * 60),
                recentInviteUses,
                recentIpUses,
                _options.AutoRevokeDetectedInvites);
        }

        private static string? HashIpAddress(string? ipAddress)
        {
            var normalized = ipAddress?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        }
    }
}
