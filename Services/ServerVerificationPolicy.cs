using DiscordCloneServer.Models;

namespace DiscordCloneServer.Services
{
    public sealed record ServerVerificationResult(
        bool Allowed,
        string Level,
        string Message);

    public static class ServerVerificationPolicy
    {
        public const string None = "none";
        public const string Low = "low";
        public const string Medium = "medium";
        public const string High = "high";
        public const string Highest = "highest";
        public const int LegacyAccountAgeMinutes = 5;
        public const int LegacyMembershipMinutes = 10;
        public const int MaxRequiredMinutes = 525600;

        private static readonly HashSet<string> Levels = new(StringComparer.OrdinalIgnoreCase)
        {
            None,
            Low,
            Medium,
            High,
            Highest
        };

        public static string NormalizeLevel(string? level)
        {
            var normalized = level?.Trim().ToLowerInvariant() ?? None;
            return Levels.Contains(normalized) ? normalized : None;
        }

        public static bool IsValidLevel(string? level)
        {
            return Levels.Contains(level?.Trim() ?? string.Empty);
        }

        public static bool IsValidRequiredMinutes(int? minutes)
        {
            return minutes is null or (>= 0 and <= MaxRequiredMinutes);
        }

        public static int NormalizeRequiredMinutes(int? minutes)
        {
            return Math.Clamp(minutes ?? 0, 0, MaxRequiredMinutes);
        }

        public static ServerVerificationResult Evaluate(
            string? level,
            Account? account,
            ServerMember? member,
            DateTime? now = null)
        {
            var normalizedLevel = NormalizeLevel(level);
            var checkedAt = now ?? DateTime.UtcNow;
            var rank = GetLevelRank(normalizedLevel);

            return EvaluateCore(
                normalizedLevel,
                account,
                member,
                checkedAt,
                requireVerifiedEmail: rank >= GetLevelRank(Low),
                minimumAccountAgeMinutes: rank >= GetLevelRank(Medium) ? LegacyAccountAgeMinutes : 0,
                minimumMembershipMinutes: rank >= GetLevelRank(High) ? LegacyMembershipMinutes : 0,
                requirePhoneNumber: normalizedLevel == Highest,
                actionName: "participating in");
        }

        public static ServerVerificationResult EvaluateJoin(
            CreateServer? server,
            Account? account,
            DateTime? now = null)
        {
            if (server == null)
            {
                return Deny(None, "Server not found.");
            }

            var normalizedLevel = NormalizeLevel(server.VerificationLevel);
            var rank = GetLevelRank(normalizedLevel);

            return EvaluateCore(
                normalizedLevel,
                account,
                member: null,
                checkedAt: now ?? DateTime.UtcNow,
                requireVerifiedEmail: server.RequireVerifiedEmail || rank >= GetLevelRank(Low),
                minimumAccountAgeMinutes: Math.Max(
                    NormalizeRequiredMinutes(server.MinimumAccountAgeMinutes),
                    rank >= GetLevelRank(Medium) ? LegacyAccountAgeMinutes : 0),
                minimumMembershipMinutes: 0,
                requirePhoneNumber: false,
                actionName: "joining");
        }

        public static ServerVerificationResult EvaluatePosting(
            CreateServer? server,
            Account? account,
            ServerMember? member,
            DateTime? now = null)
        {
            if (server == null)
            {
                return Deny(None, "Server not found.");
            }

            var normalizedLevel = NormalizeLevel(server.VerificationLevel);
            var rank = GetLevelRank(normalizedLevel);

            return EvaluateCore(
                normalizedLevel,
                account,
                member,
                checkedAt: now ?? DateTime.UtcNow,
                requireVerifiedEmail: server.RequireVerifiedEmail || rank >= GetLevelRank(Low),
                minimumAccountAgeMinutes: Math.Max(
                    NormalizeRequiredMinutes(server.MinimumAccountAgeMinutes),
                    rank >= GetLevelRank(Medium) ? LegacyAccountAgeMinutes : 0),
                minimumMembershipMinutes: Math.Max(
                    NormalizeRequiredMinutes(server.MinimumMembershipMinutes),
                    rank >= GetLevelRank(High) ? LegacyMembershipMinutes : 0),
                requirePhoneNumber: normalizedLevel == Highest,
                actionName: "posting in");
        }

        private static ServerVerificationResult EvaluateCore(
            string normalizedLevel,
            Account? account,
            ServerMember? member,
            DateTime checkedAt,
            bool requireVerifiedEmail,
            int minimumAccountAgeMinutes,
            int minimumMembershipMinutes,
            bool requirePhoneNumber,
            string actionName)
        {
            minimumAccountAgeMinutes = NormalizeRequiredMinutes(minimumAccountAgeMinutes);
            minimumMembershipMinutes = NormalizeRequiredMinutes(minimumMembershipMinutes);

            if (!requireVerifiedEmail &&
                minimumAccountAgeMinutes == 0 &&
                minimumMembershipMinutes == 0 &&
                !requirePhoneNumber)
            {
                return Allow(normalizedLevel);
            }

            if (account == null)
            {
                return Deny(normalizedLevel, "Your account could not be checked for this server's verification level.");
            }

            if (requireVerifiedEmail && account.EmailVerifiedAt == null)
            {
                return Deny(normalizedLevel, $"Verify your email before {actionName} this server.");
            }

            if (minimumAccountAgeMinutes > 0 &&
                account.CreatedAt > checkedAt.AddMinutes(-minimumAccountAgeMinutes))
            {
                return Deny(
                    normalizedLevel,
                    $"Your account must be at least {FormatMinutes(minimumAccountAgeMinutes)} old before {actionName} this server.");
            }

            if (minimumMembershipMinutes > 0 && member == null)
            {
                return Deny(normalizedLevel, $"Join this server before {actionName} it.");
            }

            if (minimumMembershipMinutes > 0 &&
                member!.JoinedAt > checkedAt.AddMinutes(-minimumMembershipMinutes))
            {
                return Deny(
                    normalizedLevel,
                    $"You must be a server member for at least {FormatMinutes(minimumMembershipMinutes)} before {actionName} this server.");
            }

            if (requirePhoneNumber && account.PhoneNumberVerifiedAt == null)
            {
                return Deny(normalizedLevel, $"Verify your phone number before {actionName} this server.");
            }

            return Allow(normalizedLevel);
        }

        private static int GetLevelRank(string level)
        {
            return NormalizeLevel(level) switch
            {
                Low => 1,
                Medium => 2,
                High => 3,
                Highest => 4,
                _ => 0
            };
        }

        private static string FormatMinutes(int minutes)
        {
            return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        }

        private static ServerVerificationResult Allow(string level)
        {
            return new ServerVerificationResult(true, level, string.Empty);
        }

        private static ServerVerificationResult Deny(string level, string message)
        {
            return new ServerVerificationResult(false, level, message);
        }
    }
}
