using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Threading.RateLimiting;

namespace DiscordCloneServer.Services
{
    public static class RateLimitPolicies
    {
        public const string Auth = "auth";
        public const string Friend = "friend";
        public const string Upload = "upload";
        public const string Abuse = "abuse";
        public const string Monitoring = "monitoring";

        public static readonly string[] Names =
        [
            Auth,
            Friend,
            Upload,
            Abuse,
            Monitoring
        ];

        private static readonly IReadOnlyDictionary<string, RateLimitPolicySettings> Defaults =
            new Dictionary<string, RateLimitPolicySettings>(StringComparer.OrdinalIgnoreCase)
            {
                [Auth] = new()
                {
                    PermitLimit = 12,
                    WindowSeconds = 60,
                    SegmentsPerWindow = 6,
                    QueueLimit = 0
                },
                [Friend] = new()
                {
                    PermitLimit = 20,
                    WindowSeconds = 60,
                    SegmentsPerWindow = 6,
                    QueueLimit = 0
                },
                [Upload] = new()
                {
                    PermitLimit = 8,
                    WindowSeconds = 60,
                    SegmentsPerWindow = 6,
                    QueueLimit = 0
                },
                [Abuse] = new()
                {
                    PermitLimit = 180,
                    WindowSeconds = 60,
                    SegmentsPerWindow = 6,
                    QueueLimit = 0
                },
                [Monitoring] = new()
                {
                    PermitLimit = 120,
                    WindowSeconds = 60,
                    SegmentsPerWindow = 6,
                    QueueLimit = 0
                }
            };

        public static RateLimitPolicySettings GetSettings(IConfiguration configuration, string policyName)
        {
            if (!Defaults.TryGetValue(policyName, out var defaults))
            {
                throw new InvalidOperationException($"Unknown rate-limit policy '{policyName}'.");
            }

            var settings = defaults.Clone();
            configuration.GetSection($"RateLimiting:Policies:{policyName}").Bind(settings);
            settings.Normalize();
            return settings;
        }

        public static SlidingWindowRateLimiterOptions CreateSlidingWindowOptions(
            IConfiguration configuration,
            string policyName)
        {
            var settings = GetSettings(configuration, policyName);

            return new SlidingWindowRateLimiterOptions
            {
                PermitLimit = settings.PermitLimit,
                Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                SegmentsPerWindow = settings.SegmentsPerWindow,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = settings.QueueLimit
            };
        }

        public static string GetClientPartitionKey(HttpContext httpContext, string policyName)
        {
            var username = httpContext.User.GetUsername();
            if (!string.IsNullOrWhiteSpace(username))
            {
                return $"{policyName}:user:{username.Trim().ToLowerInvariant()}";
            }

            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return $"{policyName}:ip:{ipAddress}";
        }
    }

    public sealed class RateLimitPolicySettings
    {
        public int PermitLimit { get; set; }
        public int WindowSeconds { get; set; }
        public int SegmentsPerWindow { get; set; }
        public int QueueLimit { get; set; }

        public RateLimitPolicySettings Clone()
        {
            return new RateLimitPolicySettings
            {
                PermitLimit = PermitLimit,
                WindowSeconds = WindowSeconds,
                SegmentsPerWindow = SegmentsPerWindow,
                QueueLimit = QueueLimit
            };
        }

        public void Normalize()
        {
            PermitLimit = Math.Max(1, PermitLimit);
            WindowSeconds = Math.Max(1, WindowSeconds);
            SegmentsPerWindow = Math.Max(1, SegmentsPerWindow);
            QueueLimit = Math.Max(0, QueueLimit);
        }
    }
}
