using DiscordCloneServer.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiscordCloneServer.Services
{
    public sealed class CleanupJobRunner
    {
        private readonly ApiContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly CleanupJobOptions _options;
        private readonly ILogger<CleanupJobRunner> _logger;

        public CleanupJobRunner(
            ApiContext context,
            IWebHostEnvironment environment,
            IOptions<CleanupJobOptions> options,
            ILogger<CleanupJobRunner> logger)
        {
            _context = context;
            _environment = environment;
            _options = options.Value;
            _logger = logger;
        }

        public Task<CleanupJobResult> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            return RunOnceAsync(DateTime.UtcNow, cancellationToken);
        }

        public async Task<CleanupJobResult> RunOnceAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var batchSize = Clamp(_options.BatchSize, 1, 5000);
            var expiredSessionsDeleted = await DeleteExpiredSessionsAsync(nowUtc, batchSize, cancellationToken);
            var contactVerificationsDeleted = await DeleteContactVerificationsAsync(nowUtc, batchSize, cancellationToken);
            var expiredAccountSecretsCleared = await ClearExpiredAccountSecretsAsync(nowUtc, batchSize, cancellationToken);
            var expiredMemberModerationStatesCleared = await ClearExpiredMemberModerationAsync(nowUtc, batchSize, cancellationToken);
            var inviteCleanup = await DeleteInactiveInvitesAsync(nowUtc, batchSize, cancellationToken);

            if (_context.ChangeTracker.HasChanges())
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            var orphanUploadsDeleted = _options.CleanupOrphanUploads
                ? await DeleteOrphanUploadsAsync(nowUtc, cancellationToken)
                : 0;

            return new CleanupJobResult(
                expiredSessionsDeleted,
                contactVerificationsDeleted,
                expiredAccountSecretsCleared,
                expiredMemberModerationStatesCleared,
                inviteCleanup.InactiveInvitesDeleted,
                inviteCleanup.StaleServerInviteLinksCleared,
                orphanUploadsDeleted);
        }

        private async Task<int> DeleteExpiredSessionsAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken cancellationToken)
        {
            var revokedCutoff = nowUtc.AddDays(-Clamp(_options.RevokedSessionRetentionDays, 0, 3650));
            var sessions = await _context.AccountSessions
                .Where(session =>
                    session.ExpiresAt <= nowUtc ||
                    (session.RevokedAt != null && session.RevokedAt <= revokedCutoff))
                .OrderBy(session => session.ExpiresAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            _context.AccountSessions.RemoveRange(sessions);
            return sessions.Count;
        }

        private async Task<int> DeleteContactVerificationsAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken cancellationToken)
        {
            var consumedCutoff = nowUtc.AddDays(-Clamp(_options.ConsumedVerificationRetentionDays, 0, 3650));
            var verifications = await _context.ContactVerifications
                .Where(verification =>
                    verification.ExpiresAt <= nowUtc ||
                    (verification.ConsumedAt != null && verification.ConsumedAt <= consumedCutoff))
                .OrderBy(verification => verification.ExpiresAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            _context.ContactVerifications.RemoveRange(verifications);
            return verifications.Count;
        }

        private async Task<int> ClearExpiredAccountSecretsAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken cancellationToken)
        {
            var accounts = await _context.Accounts
                .Where(account =>
                    (account.PasswordResetExpiresAt != null && account.PasswordResetExpiresAt <= nowUtc) ||
                    (account.TwoFactorLoginTicketExpiresAt != null && account.TwoFactorLoginTicketExpiresAt <= nowUtc))
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            var changed = 0;
            foreach (var account in accounts)
            {
                var accountChanged = false;
                if (account.PasswordResetExpiresAt <= nowUtc)
                {
                    account.PasswordResetTokenHash = null;
                    account.PasswordResetExpiresAt = null;
                    accountChanged = true;
                }

                if (account.TwoFactorLoginTicketExpiresAt <= nowUtc)
                {
                    account.TwoFactorLoginTicketHash = null;
                    account.TwoFactorLoginTicketExpiresAt = null;
                    accountChanged = true;
                }

                if (accountChanged)
                {
                    changed += 1;
                }
            }

            return changed;
        }

        private async Task<int> ClearExpiredMemberModerationAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken cancellationToken)
        {
            var members = await _context.ServerMembers
                .Where(member =>
                    (member.IsMuted && member.MutedUntil != null && member.MutedUntil <= nowUtc) ||
                    (member.TimedOutUntil != null && member.TimedOutUntil <= nowUtc))
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            var changed = 0;
            foreach (var member in members)
            {
                var memberChanged = false;
                if (member.IsMuted && member.MutedUntil <= nowUtc)
                {
                    member.IsMuted = false;
                    member.MutedUntil = null;
                    memberChanged = true;
                }

                if (member.TimedOutUntil <= nowUtc)
                {
                    member.TimedOutUntil = null;
                    memberChanged = true;
                }

                if (memberChanged)
                {
                    changed += 1;
                }
            }

            return changed;
        }

        private async Task<(int InactiveInvitesDeleted, int StaleServerInviteLinksCleared)> DeleteInactiveInvitesAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken cancellationToken)
        {
            var inactiveCutoff = nowUtc.AddDays(-Clamp(_options.InactiveInviteRetentionDays, 0, 3650));
            var invites = await _context.ServerInvites
                .Where(invite =>
                    (invite.RevokedAt != null && invite.RevokedAt <= inactiveCutoff) ||
                    (invite.ExpiresAt != null && invite.ExpiresAt <= inactiveCutoff) ||
                    (invite.MaxUses != null && invite.Uses >= invite.MaxUses && invite.CreatedAt <= inactiveCutoff))
                .OrderBy(invite => invite.CreatedAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (invites.Count == 0)
            {
                return (0, 0);
            }

            var inactiveCodes = invites
                .Select(invite => invite.Code)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var staleLinksCleared = 0;
            var servers = await _context.CreateServers
                .Where(server => server.InviteLink != null && server.InviteLink != "")
                .ToListAsync(cancellationToken);

            foreach (var server in servers)
            {
                var linkedCode = ExtractInviteCode(server.InviteLink);
                if (linkedCode != null && inactiveCodes.Contains(linkedCode))
                {
                    server.InviteLink = null;
                    staleLinksCleared += 1;
                }
            }

            _context.ServerInvites.RemoveRange(invites);
            return (invites.Count, staleLinksCleared);
        }

        private async Task<int> DeleteOrphanUploadsAsync(DateTime nowUtc, CancellationToken cancellationToken)
        {
            var uploadsFolder = GetUploadsFolder();
            if (!Directory.Exists(uploadsFolder))
            {
                return 0;
            }

            var cutoff = nowUtc.AddHours(-Clamp(_options.OrphanUploadRetentionHours, 1, 24 * 365));
            var referencedFiles = await GetReferencedUploadFileNamesAsync(cancellationToken);
            var deleted = 0;

            foreach (var filePath in Directory.EnumerateFiles(uploadsFolder))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileName = Path.GetFileName(filePath);
                if (string.IsNullOrWhiteSpace(fileName) || referencedFiles.Contains(fileName))
                {
                    continue;
                }

                var lastTouchedAt = MaxUtc(File.GetCreationTimeUtc(filePath), File.GetLastWriteTimeUtc(filePath));
                if (lastTouchedAt > cutoff)
                {
                    continue;
                }

                try
                {
                    File.Delete(filePath);
                    deleted += 1;
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Could not delete orphan upload {UploadPath}", filePath);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning(ex, "Could not delete orphan upload {UploadPath}", filePath);
                }
            }

            return deleted;
        }

        private async Task<HashSet<string>> GetReferencedUploadFileNamesAsync(CancellationToken cancellationToken)
        {
            var referencedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var accountUploads = await _context.Accounts
                .Select(account => new { account.ProfilePictureUrl, account.ProfileBannerUrl })
                .ToListAsync(cancellationToken);
            foreach (var account in accountUploads)
            {
                AddUploadReference(referencedFiles, account.ProfilePictureUrl);
                AddUploadReference(referencedFiles, account.ProfileBannerUrl);
            }

            var serverMessageUploads = await _context.ServerMessages
                .Where(message => message.AttachmentUrl != null && message.AttachmentUrl != "")
                .Select(message => message.AttachmentUrl)
                .ToListAsync(cancellationToken);
            foreach (var uploadUrl in serverMessageUploads)
            {
                AddUploadReference(referencedFiles, uploadUrl);
            }

            var privateMessageUploads = await _context.PrivateMessageFriends
                .Where(message => message.AttachmentUrl != null && message.AttachmentUrl != "")
                .Select(message => message.AttachmentUrl)
                .ToListAsync(cancellationToken);
            foreach (var uploadUrl in privateMessageUploads)
            {
                AddUploadReference(referencedFiles, uploadUrl);
            }

            var groupMessageUploads = await _context.GroupMessages
                .Where(message => message.AttachmentUrl != null && message.AttachmentUrl != "")
                .Select(message => message.AttachmentUrl)
                .ToListAsync(cancellationToken);
            foreach (var uploadUrl in groupMessageUploads)
            {
                AddUploadReference(referencedFiles, uploadUrl);
            }

            return referencedFiles;
        }

        private string GetUploadsFolder()
        {
            var webRootPath = _environment.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRootPath))
            {
                webRootPath = Path.Combine(_environment.ContentRootPath, "wwwroot");
            }

            return Path.Combine(webRootPath, "uploads");
        }

        private static void AddUploadReference(HashSet<string> referencedFiles, string? uploadUrl)
        {
            var fileName = ExtractUploadFileName(uploadUrl);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                referencedFiles.Add(fileName);
            }
        }

        private static string? ExtractUploadFileName(string? uploadUrl)
        {
            var value = uploadUrl?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                value = uri.AbsolutePath;
            }

            var queryIndex = value.IndexOfAny(new[] { '?', '#' });
            if (queryIndex >= 0)
            {
                value = value[..queryIndex];
            }

            value = value.Replace('\\', '/');
            const string marker = "/uploads/";
            var markerIndex = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return null;
            }

            var fileName = value[(markerIndex + marker.Length)..];
            if (fileName.Contains('/'))
            {
                return null;
            }

            fileName = Uri.UnescapeDataString(fileName);
            return string.IsNullOrWhiteSpace(fileName) ? null : Path.GetFileName(fileName);
        }

        private static string? ExtractInviteCode(string? inviteLink)
        {
            var trimmed = inviteLink?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }

            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                return uri.Segments.LastOrDefault()?.Trim('/');
            }

            var slashIndex = trimmed.LastIndexOf('/');
            return slashIndex >= 0 ? trimmed[(slashIndex + 1)..] : trimmed;
        }

        private static DateTime MaxUtc(DateTime left, DateTime right)
        {
            return left > right ? left : right;
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Min(max, Math.Max(min, value));
        }
    }
}
