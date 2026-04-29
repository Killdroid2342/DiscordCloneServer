using System.Net.Http.Json;

namespace DiscordCloneServer.Services
{
    public sealed record ContactVerificationMessage(
        string Kind,
        string Target,
        string Username,
        string Code,
        DateTime ExpiresAt);

    public interface IContactVerificationDelivery
    {
        Task SendAsync(ContactVerificationMessage message, CancellationToken cancellationToken = default);
    }

    public class ContactVerificationDelivery : IContactVerificationDelivery
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly ILogger<ContactVerificationDelivery> _logger;

        public ContactVerificationDelivery(
            HttpClient httpClient,
            IConfiguration config,
            ILogger<ContactVerificationDelivery> logger)
        {
            _httpClient = httpClient;
            _config = config;
            _logger = logger;
        }

        public async Task SendAsync(ContactVerificationMessage message, CancellationToken cancellationToken = default)
        {
            var providerSection = message.Kind.Equals("phone", StringComparison.OrdinalIgnoreCase)
                ? "Verification:Sms"
                : "Verification:Email";
            var endpoint = _config[$"{providerSection}:WebhookUrl"];

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                _logger.LogWarning(
                    "{Kind} verification code for {Target} ({Username}) is {Code}. Configure {Section}:WebhookUrl for production delivery.",
                    message.Kind,
                    message.Target,
                    message.Username,
                    message.Code,
                    providerSection);
                return;
            }

            var apiKey = _config[$"{providerSection}:ApiKey"];
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(new
                {
                    message.Kind,
                    message.Target,
                    message.Username,
                    message.Code,
                    message.ExpiresAt
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
}
