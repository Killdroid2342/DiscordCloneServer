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
