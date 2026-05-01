using DiscordCloneServer.Data;
using DiscordCloneServer.Models;
using DiscordCloneServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DiscordCloneServer.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    [Authorize]
    public class ReportsController : ControllerBase
    {
        private readonly ApiContext _context;
        private static readonly string[] ValidReasons =
        {
            "spam",
            "harassment",
            "hate",
            "explicit",
            "threat",
            "impersonation",
            "other"
        };

        private static readonly string[] ValidStatuses =
        {
            "open",
            "reviewed",
            "resolved",
            "dismissed"
        };

        public ReportsController(ApiContext context)
        {
            _context = context;
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> SubmitReport([FromBody] ReportCreateRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });

            var reason = NormalizeReason(request.Reason);
            if (reason == null)
                return BadRequest(new { message = "Choose a valid report reason." });

            var targetType = NormalizeTargetType(request.TargetType);
            if (targetType == null)
                return BadRequest(new { message = "Report target must be a message or user." });

            var scopeType = NormalizeScopeType(request.ScopeType);
            if (scopeType == null)
                return BadRequest(new { message = "Report scope is invalid." });

            var description = NormalizeOptional(request.Description);
            if (description?.Length > 1000)
                return BadRequest(new { message = "Report details must be 1000 characters or less." });

            var report = new UserReport
            {
                Id = Guid.NewGuid().ToString(),
                ScopeType = scopeType,
                TargetType = targetType,
                ReportedByUsername = currentUsername,
                Reason = reason,
                Description = description,
                Status = "open",
                CreatedAt = DateTime.UtcNow
            };

            var validation = targetType == "message"
                ? await PopulateMessageReport(report, request, currentUsername)
                : await PopulateUserReport(report, request, currentUsername);

            if (validation != null)
                return validation;

            _context.UserReports.Add(report);
            await _context.SaveChangesAsync();

            return Ok(BuildReportResponse(report));
        }

        [HttpGet]
        public async Task<IActionResult> GetMyReports(int take = 50)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });

            take = Math.Clamp(take, 1, 100);
            var reports = await _context.UserReports
                .Where(report => report.ReportedByUsername == currentUsername)
                .OrderByDescending(report => report.CreatedAt)
                .Take(take)
                .ToListAsync();

            return Ok(reports.Select(BuildReportResponse));
        }

        [HttpGet]
        public async Task<IActionResult> GetServerReports(string serverId, string? status = "open", int take = 75)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });

            serverId = serverId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(serverId))
                return BadRequest(new { message = "Server id is required." });

            if (!await CanReviewServerReports(serverId, currentUsername))
                return Forbid();

            var normalizedStatus = NormalizeStatus(status, allowAll: true);
            if (normalizedStatus == null)
                return BadRequest(new { message = "Report status is invalid." });

            take = Math.Clamp(take, 1, 150);
            var query = _context.UserReports
                .Where(report => report.ServerId == serverId);

            if (normalizedStatus != "all")
            {
                query = query.Where(report => report.Status == normalizedStatus);
            }

            var reports = await query
                .OrderByDescending(report => report.CreatedAt)
                .Take(take)
                .ToListAsync();

            return Ok(reports.Select(BuildReportResponse));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateReportStatus([FromBody] ReportStatusUpdateRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });

            var serverId = request.ServerId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(serverId))
                return BadRequest(new { message = "Server id is required." });

            if (!await CanReviewServerReports(serverId, currentUsername))
                return Forbid();

            var nextStatus = NormalizeStatus(request.Status, allowAll: false);
            if (nextStatus == null)
                return BadRequest(new { message = "Report status is invalid." });

            var report = await _context.UserReports.FirstOrDefaultAsync(item =>
                item.Id == request.ReportId && item.ServerId == serverId);
            if (report == null)
                return NotFound(new { message = "Report not found." });

            var note = NormalizeOptional(request.ResolutionNote);
            if (note?.Length > 1000)
                return BadRequest(new { message = "Resolution note must be 1000 characters or less." });

            var previousStatus = report.Status;
            report.Status = nextStatus;
            report.ReviewedByUsername = currentUsername;
            report.ReviewedAt = DateTime.UtcNow;
            report.ResolutionNote = note;

            AddAuditLog(
                serverId,
                "report_status_updated",
                currentUsername,
                "report",
                report.Id,
                report.TargetUsername,
                new
                {
                    PreviousStatus = previousStatus,
                    CurrentStatus = nextStatus,
                    report.TargetType,
                    report.MessageId,
                    report.Reason
                });

            await _context.SaveChangesAsync();
            return Ok(BuildReportResponse(report));
        }

        private async Task<IActionResult?> PopulateMessageReport(
            UserReport report,
            ReportCreateRequest request,
            string currentUsername)
        {
            var messageId = request.MessageId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(messageId))
                return BadRequest(new { message = "Message id is required." });

            switch (report.ScopeType)
            {
                case "server":
                    return await PopulateServerMessageReport(report, request, currentUsername, messageId);
                case "dm":
                    return await PopulateDmMessageReport(report, currentUsername, messageId);
                case "group":
                    return await PopulateGroupMessageReport(report, request, currentUsername, messageId);
                default:
                    return BadRequest(new { message = "Message reports must be server, DM, or group scoped." });
            }
        }

        private async Task<IActionResult?> PopulateServerMessageReport(
            UserReport report,
            ReportCreateRequest request,
            string currentUsername,
            string messageId)
        {
            var message = await _context.ServerMessages.FirstOrDefaultAsync(item => item.MessageID == messageId);
            if (message == null)
                return NotFound(new { message = "Message not found." });

            var channel = await _context.Channels.FirstOrDefaultAsync(item => item.Id == message.ChannelId);
            if (channel == null)
                return NotFound(new { message = "Channel not found." });

            if (!string.IsNullOrWhiteSpace(request.ServerId) &&
                !string.Equals(request.ServerId.Trim(), channel.ServerId, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Message is not in the requested server." });

            if (!await IsServerMember(channel.ServerId, currentUsername))
                return Forbid();
            if (IsSameUsername(message.MessagesUserSender, currentUsername))
                return BadRequest(new { message = "You cannot report your own message." });

            report.ServerId = channel.ServerId;
            report.ChannelId = channel.Id;
            report.MessageId = message.MessageID;
            report.TargetUsername = message.MessagesUserSender;
            report.MessagePreview = BuildPreview(message.userText, message.AttachmentUrl);
            return null;
        }

        private async Task<IActionResult?> PopulateDmMessageReport(
            UserReport report,
            string currentUsername,
            string messageId)
        {
            var message = await _context.PrivateMessageFriends.FirstOrDefaultAsync(item => item.PrivateMessageID == messageId);
            if (message == null)
                return NotFound(new { message = "Message not found." });

            if (!string.Equals(message.MessagesUserSender, currentUsername, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(message.MessageUserReciver, currentUsername, StringComparison.OrdinalIgnoreCase))
                return Forbid();
            if (IsSameUsername(message.MessagesUserSender, currentUsername))
                return BadRequest(new { message = "You cannot report your own message." });

            report.MessageId = message.PrivateMessageID;
            report.TargetUsername = message.MessagesUserSender;
            report.MessagePreview = BuildPreview(message.FriendMessagesData, message.AttachmentUrl);
            return null;
        }

        private async Task<IActionResult?> PopulateGroupMessageReport(
            UserReport report,
            ReportCreateRequest request,
            string currentUsername,
            string messageId)
        {
            if (!Guid.TryParse(messageId, out var parsedMessageId))
                return BadRequest(new { message = "Message id is invalid." });

            var message = await _context.GroupMessages.FirstOrDefaultAsync(item => item.Id == parsedMessageId);
            if (message == null)
                return NotFound(new { message = "Message not found." });

            if (!string.IsNullOrWhiteSpace(request.GroupId) &&
                !string.Equals(request.GroupId.Trim(), message.GroupId.ToString(), StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Message is not in the requested group." });

            if (!await IsGroupMember(message.GroupId, currentUsername))
                return Forbid();
            if (IsSameUsername(message.Sender, currentUsername))
                return BadRequest(new { message = "You cannot report your own message." });

            report.GroupId = message.GroupId.ToString();
            report.MessageId = message.Id.ToString();
            report.TargetUsername = message.Sender;
            report.MessagePreview = BuildPreview(message.Content, message.AttachmentUrl);
            return null;
        }

        private async Task<IActionResult?> PopulateUserReport(
            UserReport report,
            ReportCreateRequest request,
            string currentUsername)
        {
            var targetUsername = request.TargetUsername?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(targetUsername))
                return BadRequest(new { message = "Target username is required." });
            if (string.Equals(targetUsername, currentUsername, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "You cannot report yourself." });

            var targetAccountExists = await _context.Accounts.AnyAsync(account =>
                account.UserName == targetUsername && !account.IsDisabled);
            if (!targetAccountExists)
                return NotFound(new { message = "Account not found." });

            if (report.ScopeType == "server")
            {
                var serverId = request.ServerId?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(serverId))
                    return BadRequest(new { message = "Server id is required for server user reports." });
                if (!await IsServerMember(serverId, currentUsername) || !await IsServerMember(serverId, targetUsername))
                    return Forbid();
                report.ServerId = serverId;
            }
            else if (report.ScopeType == "group")
            {
                if (!Guid.TryParse(request.GroupId, out var groupId))
                    return BadRequest(new { message = "Group id is required for group user reports." });
                if (!await IsGroupMember(groupId, currentUsername))
                    return Forbid();
                report.GroupId = groupId.ToString();
            }
            else
            {
                report.ScopeType = "account";
            }

            report.TargetUsername = targetUsername;
            return null;
        }

        private async Task<bool> CanReviewServerReports(string serverId, string username)
        {
            var server = await _context.CreateServers.FirstOrDefaultAsync(server => server.ServerID == serverId);
            if (server == null)
            {
                return false;
            }

            if (string.Equals(server.ServerOwner, username, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var member = await _context.ServerMembers.FirstOrDefaultAsync(member =>
                member.ServerId == serverId && member.Username == username);
            if (member == null)
            {
                return false;
            }

            var roleName = NormalizeRoleName(member.Role);
            var role = await _context.ServerRoles.FirstOrDefaultAsync(role =>
                role.ServerId == serverId && role.Name == roleName);
            var canReview = roleName is "owner" or "admin" or "moderator" ||
                            role?.CanManageServer == true ||
                            role?.CanManageMembers == true ||
                            role?.CanBanMembers == true;

            if (!canReview)
            {
                return false;
            }

            if (!server.RequireTwoFactorForModerators)
            {
                return true;
            }

            var account = await _context.Accounts.FirstOrDefaultAsync(account =>
                account.UserName == username && !account.IsDisabled);
            return account?.TwoFactorEnabled == true;
        }

        private async Task<bool> IsServerMember(string serverId, string username)
        {
            return await _context.ServerMembers.AnyAsync(member =>
                member.ServerId == serverId && member.Username == username);
        }

        private async Task<bool> IsGroupMember(Guid groupId, string username)
        {
            var group = await _context.GroupChats.FirstOrDefaultAsync(group => group.Id == groupId);
            return group?.Members.Any(member => string.Equals(member, username, StringComparison.OrdinalIgnoreCase)) == true;
        }

        private void AddAuditLog(
            string serverId,
            string actionType,
            string actorUsername,
            string? targetType = null,
            string? targetId = null,
            string? targetUsername = null,
            object? details = null)
        {
            _context.ServerAuditLogs.Add(new ServerAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                ServerId = serverId,
                ActionType = actionType,
                ActorUsername = actorUsername,
                TargetType = NormalizeOptional(targetType),
                TargetId = NormalizeOptional(targetId),
                TargetUsername = NormalizeOptional(targetUsername),
                DetailsJson = details == null ? null : System.Text.Json.JsonSerializer.Serialize(details),
                CreatedAt = DateTime.UtcNow
            });
        }

        private static object BuildReportResponse(UserReport report)
        {
            return new
            {
                report.Id,
                report.ScopeType,
                report.TargetType,
                report.ServerId,
                report.ChannelId,
                report.GroupId,
                report.MessageId,
                report.TargetUsername,
                report.ReportedByUsername,
                report.Reason,
                report.Description,
                report.MessagePreview,
                report.Status,
                report.CreatedAt,
                report.ReviewedByUsername,
                report.ReviewedAt,
                report.ResolutionNote
            };
        }

        private static string? NormalizeScopeType(string? value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            return normalized is "server" or "dm" or "group" or "account" ? normalized : null;
        }

        private static string? NormalizeTargetType(string? value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            return normalized is "message" or "user" ? normalized : null;
        }

        private static string? NormalizeReason(string? value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            return ValidReasons.Contains(normalized) ? normalized : null;
        }

        private static string? NormalizeStatus(string? value, bool allowAll)
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? "open"
                : value.Trim().ToLowerInvariant();
            if (allowAll && normalized == "all")
            {
                return normalized;
            }

            return ValidStatuses.Contains(normalized) ? normalized : null;
        }

        private static string NormalizeRoleName(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '-');
        }

        private static bool IsSameUsername(string? left, string? right)
        {
            return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string? NormalizeOptional(string? value)
        {
            var normalized = value?.Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private static string? BuildPreview(string? text, string? attachmentUrl)
        {
            var normalized = NormalizeOptional(text);
            if (string.IsNullOrWhiteSpace(normalized) && !string.IsNullOrWhiteSpace(attachmentUrl))
            {
                normalized = "[Attachment]";
            }

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            return normalized.Length <= 500 ? normalized : normalized[..500];
        }
    }

    public class ReportCreateRequest
    {
        public string ScopeType { get; set; } = "server";
        public string TargetType { get; set; } = "message";
        public string? ServerId { get; set; }
        public string? ChannelId { get; set; }
        public string? GroupId { get; set; }
        public string? MessageId { get; set; }
        public string? TargetUsername { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class ReportStatusUpdateRequest
    {
        public string ServerId { get; set; } = string.Empty;
        public string ReportId { get; set; } = string.Empty;
        public string Status { get; set; } = "reviewed";
        public string? ResolutionNote { get; set; }
    }
}
