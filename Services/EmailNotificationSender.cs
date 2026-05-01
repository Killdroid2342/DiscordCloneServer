using System.Net.Http.Json;
using System.Text.Json;
using DiscordCloneServer.Models;

namespace DiscordCloneServer.Services
{
    public sealed record EmailNotificationRequest(
        string SenderUsername,
        string Subject,
        string Preview,
        string Scope,
        string ConversationName,
        string? ConversationId,
        string? MessageId,
        DateTime CreatedAt);

    public interface IEmailNotificationSender
    {
        Task SendToAccountAsync(
            Account recipient,
            EmailNotificationRequest notification,
            CancellationToken cancellationToken = default);
    }

    public class EmailNotificationSender : IEmailNotificationSender
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly ILogger<EmailNotificationSender> _logger;

        public EmailNotificationSender(
            HttpClient httpClient,
            IConfiguration config,
            ILogger<EmailNotificationSender> logger)
        {
            _httpClient = httpClient;
            _config = config;
            _logger = logger;
        }

        public async Task SendToAccountAsync(
            Account recipient,
            EmailNotificationRequest notification,
            CancellationToken cancellationToken = default)
        {
            if (!EmailNotificationPreferences.CanReceiveEmailNotifications(recipient))
            {
                return;
            }

            var endpoint = _config["Notifications:Email:WebhookUrl"];
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                _logger.LogInformation(
                    "Email notification for {RecipientUsername} skipped. Configure Notifications:Email:WebhookUrl for delivery.",
                    recipient.UserName);
                return;
            }

            var apiKey = _config["Notifications:Email:ApiKey"];
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(new
                {
                    Target = recipient.Email,
                    RecipientUsername = recipient.UserName,
                    notification.SenderUsername,
                    notification.Subject,
                    notification.Preview,
                    notification.Scope,
                    notification.ConversationName,
                    notification.ConversationId,
                    notification.MessageId,
                    notification.CreatedAt
                })
            };

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    public static class EmailNotificationPreferences
    {
        public const string ToggleKey = "emailNotifications";

        public static bool CanReceiveEmailNotifications(Account account)
        {
            return !account.IsDisabled &&
                   account.EmailVerifiedAt != null &&
                   !string.IsNullOrWhiteSpace(account.Email) &&
                   IsEmailNotificationsEnabled(account.SettingsJson);
        }

        public static bool IsEmailNotificationsEnabled(string? settingsJson)
        {
            if (string.IsNullOrWhiteSpace(settingsJson))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(settingsJson);
                var root = doc.RootElement;
                if (TryGetBoolean(root, ToggleKey, out var rootValue))
                {
                    return rootValue;
                }

                if (root.TryGetProperty("toggles", out var toggles) &&
                    toggles.ValueKind == JsonValueKind.Object &&
                    TryGetBoolean(toggles, ToggleKey, out var toggleValue))
                {
                    return toggleValue;
                }
            }
            catch (JsonException)
            {
                return false;
            }

            return false;
        }

        public static string BuildPreview(string? text, string? attachmentUrl = null)
        {
            var normalizedText = NormalizeWhitespace(text);
            if (string.IsNullOrWhiteSpace(normalizedText))
            {
                normalizedText = string.IsNullOrWhiteSpace(attachmentUrl)
                    ? "Sent a message."
                    : "Sent an attachment.";
            }

            return normalizedText.Length <= 240
                ? normalizedText
                : $"{normalizedText[..237].Trim()}...";
        }

        private static bool TryGetBoolean(JsonElement element, string propertyName, out bool value)
        {
            value = false;
            if (!element.TryGetProperty(propertyName, out var property))
            {
                return false;
            }

            if (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
            {
                value = property.GetBoolean();
                return true;
            }

            return false;
        }

        private static string NormalizeWhitespace(string? value)
        {
            return string.Join(
                " ",
                (value ?? string.Empty)
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
