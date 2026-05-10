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

        public async Task SendPrivateEmailNotificationAsync(
            string messageId,
            CancellationToken cancellationToken = default)
        {
            var message = await _context.PrivateMessageFriends.FirstOrDefaultAsync(
                item => item.PrivateMessageID == messageId,
                cancellationToken);
            if (message == null)
            {
                return;
            }

            var recipient = await _context.Accounts.FirstOrDefaultAsync(
                account => account.UserName == message.MessageUserReciver && !account.IsDisabled,
                cancellationToken);
            if (recipient == null)
            {
                return;
            }

            await SendSafelyAsync(
                recipient,
                new EmailNotificationRequest(
                    message.MessagesUserSender,
                    $"New message from {message.MessagesUserSender}",
                    EmailNotificationPreferences.BuildPreview(
                        message.FriendMessagesData,
                        message.AttachmentUrl),
                    "dm",
                    message.MessagesUserSender,
                    BuildDmScopeId(message.MessagesUserSender, message.MessageUserReciver),
                    message.PrivateMessageID,
                    ParseDate(message.Date)),
                cancellationToken);
        }

        public async Task SendGroupEmailNotificationsAsync(
            string messageId,
            CancellationToken cancellationToken = default)
        {
            if (!Guid.TryParse(messageId, out var parsedMessageId))
            {
                return;
            }

            var message = await _context.GroupMessages.FirstOrDefaultAsync(
                item => item.Id == parsedMessageId,
                cancellationToken);
            if (message == null)
            {
                return;
            }

            var group = await _context.GroupChats.FirstOrDefaultAsync(
                item => item.Id == message.GroupId,
                cancellationToken);
            if (group == null)
            {
                return;
            }

            var recipientNames = group.Members
                .Where(member => !string.Equals(member, message.Sender, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (recipientNames.Length == 0)
            {
                return;
            }

            var recipients = await _context.Accounts
                .Where(account => recipientNames.Contains(account.UserName) && !account.IsDisabled)
                .ToListAsync(cancellationToken);

            foreach (var recipient in recipients)
            {
                await SendSafelyAsync(
                    recipient,
                    new EmailNotificationRequest(
                        message.Sender,
                        $"New message in {group.Name}",
                        EmailNotificationPreferences.BuildPreview(message.Content, message.AttachmentUrl),
                        "group",
                        group.Name,
                        group.Id.ToString(),
                        message.Id.ToString(),
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
