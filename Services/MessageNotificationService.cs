using System.Text.RegularExpressions;
using DiscordCloneServer.Data;
using DiscordCloneServer.Models;
using Microsoft.EntityFrameworkCore;

namespace DiscordCloneServer.Services
{
    public interface IMessageNotificationService
    {
        Task SendServerMentionEmailNotificationsAsync(string messageId, CancellationToken cancellationToken = default);
        Task SendPrivateEmailNotificationAsync(string messageId, CancellationToken cancellationToken = default);
        Task SendGroupEmailNotificationsAsync(string messageId, CancellationToken cancellationToken = default);
    }

    public sealed class MessageNotificationService : IMessageNotificationService
    {
        private readonly ApiContext _context;
        private readonly IEmailNotificationSender _emailNotificationSender;
        private readonly ILogger<MessageNotificationService> _logger;

        public MessageNotificationService(
            ApiContext context,
            IEmailNotificationSender emailNotificationSender,
            ILogger<MessageNotificationService> logger)
        {
            _context = context;
            _emailNotificationSender = emailNotificationSender;
            _logger = logger;
        }

        public async Task SendServerMentionEmailNotificationsAsync(
            string messageId,
            CancellationToken cancellationToken = default)
        {
            var message = await _context.ServerMessages.FirstOrDefaultAsync(
                item => item.MessageID == messageId,
                cancellationToken);
            if (message == null)
            {
                return;
            }

            var channel = await _context.Channels.FirstOrDefaultAsync(
                item => item.Id == message.ChannelId,
                cancellationToken);
            if (channel == null)
            {
                return;
            }

            var mentionNames = ExtractMentions(message.userText)
                .Where(mention =>
                    !mention.Equals("everyone", StringComparison.OrdinalIgnoreCase) &&
                    !mention.Equals("here", StringComparison.OrdinalIgnoreCase) &&
                    !mention.Equals(message.MessagesUserSender, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (mentionNames.Length == 0)
            {
                return;
            }

            var serverMemberNames = await _context.ServerMembers
                .Where(member => member.ServerId == channel.ServerId)
                .Select(member => member.Username)
                .ToListAsync(cancellationToken);
            var recipientNames = serverMemberNames
                .Where(member => mentionNames.Contains(member, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (recipientNames.Length == 0)
            {
                return;
            }

            var accounts = await _context.Accounts
                .Where(account => !account.IsDisabled)
                .ToListAsync(cancellationToken);
            var recipients = accounts
                .Where(account => recipientNames.Contains(account.UserName, StringComparer.OrdinalIgnoreCase));

            foreach (var recipient in recipients)
            {
                await SendSafelyAsync(
                    recipient,
                    new EmailNotificationRequest(
                        message.MessagesUserSender,
                        $"{message.MessagesUserSender} mentioned you in #{channel.Name}",
                        EmailNotificationPreferences.BuildPreview(message.userText, message.AttachmentUrl),
                        "server",
                        channel.Name,
                        channel.Id,
                        message.MessageID,
                        ParseDate(message.Date)),
                    cancellationToken);
            }
        }

        
        private async Task SendSafelyAsync(
            Account recipient,
            EmailNotificationRequest notification,
            CancellationToken cancellationToken)
        {
            try
            {
                await _emailNotificationSender.SendToAccountAsync(recipient, notification, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not send {Scope} email notification {MessageId} to {RecipientUsername}",
                    notification.Scope,
                    notification.MessageId,
                    recipient.UserName);
            }
        }

        private static string[] ExtractMentions(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<string>();
            }

            return Regex
                .Matches(text, @"@([A-Za-z0-9_.-]{3,32})")
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string BuildDmScopeId(string left, string right)
        {
            var users = new[] { left, right }
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return string.Join(":", users);
        }

        private static DateTime ParseDate(string? date)
        {
            return DateTime.TryParse(date, out var dt) ? dt : DateTime.MinValue;
        }
    }
}
