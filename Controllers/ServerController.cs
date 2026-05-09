using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using DiscordCloneServer.Data;
using DiscordCloneServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;

using DiscordCloneServer.Hubs;
using DiscordCloneServer.Services;

namespace DiscordCloneServer.Controllers
{
    public class ServerIdComparer : IEqualityComparer<CreateServer>
    {
        public bool Equals(CreateServer? x, CreateServer? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return x.ServerID == y.ServerID;
        }

        public int GetHashCode([DisallowNull] CreateServer obj)
        {
            return obj.ServerID?.GetHashCode() ?? 0;
        }
    }

    [Route("api/[controller]/[action]")]
    [ApiController]
    [Authorize]
    public class ServerController : ControllerBase
    {
        private readonly ApiContext _context;
        private readonly IConfiguration _config;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IMemoryCache? _cache;
        private readonly IInviteAbuseDetectionService? _inviteAbuseDetectionService;
        private readonly TimeSpan _publicDiscoveryCacheDuration;
        private const int MaxModerationDurationMinutes = 60 * 24 * 28;
        private const int MaxDiscoveryTags = 8;
        private const int MaxDiscoveryTagLength = 32;
        private const int MaxWelcomeMessageLength = 600;
        private const int MaxWelcomeChecklistItems = 6;
        private const int MaxWelcomeChecklistItemLength = 120;
        private const int MaxProfileBadgeCount = 6;
        private static long _publicDiscoveryCacheVersion;
        private static readonly HashSet<string> ProfileBadgeIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "early-member",
            "community-helper",
            "server-builder",
            "bug-hunter",
            "developer",
            "artist",
            "gamer",
            "music-fan"
        };

        private sealed record ServerTemplateChannelDefinition(string Name, string Type);
        private sealed record ServerTemplateCategoryDefinition(string Name, IReadOnlyList<ServerTemplateChannelDefinition> Channels);
        private sealed record ServerTemplateDefinition(
            string Id,
            string Name,
            string Description,
            string DiscoveryCategory,
            string[] WelcomeChecklist,
            IReadOnlyList<ServerTemplateCategoryDefinition> Categories);
        private sealed record PublicServerDiscoveryCacheItem(
            IReadOnlyList<CreateServer> Servers,
            Dictionary<string, int> MemberCounts,
            Dictionary<string, int> ChannelCounts);
        private sealed record NormalizedAutoModRule(
            string Name,
            string TriggerType,
            string TriggerValue,
            string ActionType,
            bool IsEnabled);

        private static readonly ServerTemplateDefinition[] ServerTemplates =
        {
            new(
                "friends",
                "Friends",
                "A simple server for a small group.",
                "community",
                new[]
                {
                    "Read the welcome message",
                    "Say hello in the general channel",
                    "Join a voice channel when you are ready"
                },
                new[]
                {
                    new ServerTemplateCategoryDefinition(
                        "Text Channels",
                        new[]
                        {
                            new ServerTemplateChannelDefinition("general", "text")
                        }),
                    new ServerTemplateCategoryDefinition(
                        "Voice Channels",
                        new[]
                        {
                            new ServerTemplateChannelDefinition("General", "voice")
                        }),
                    new ServerTemplateCategoryDefinition(
                        "Stage Channels",
                        new[]
                        {
                            new ServerTemplateChannelDefinition("Town Hall", "stage")
                        })
                }),
            new(
                "community",
                "Community",
                "Announcements, intros, events, and public gathering spaces.",
                "community",
                new[]
                {
                    "Read the rules",
                    "Introduce yourself",
                    "Check upcoming events"
                },
                new[]
                {
                    new ServerTemplateCategoryDefinition(
                        "Information",
                        new[]
                        {
                            new ServerTemplateChannelDefinition("rules", "text"),
                            new ServerTemplateChannelDefinition("announcements", "text")
                        }),
                    new ServerTemplateCategoryDefinition(
                        "Community",
                        new[]
                        {
                            new ServerTemplateChannelDefinition("general", "text"),
                            new ServerTemplateChannelDefinition("introductions", "text"),
                            new ServerTemplateChannelDefinition("events", "text")
                        }),
                    new ServerTemplateCategoryDefinition(
                        "Voice",
                        new[]
                        {
                            new ServerTemplateChannelDefinition("Lounge", "voice"),
                            new ServerTemplateChannelDefinition("Town Hall", "stage")
                        })
                }),
            new(
                "gaming",
                "Gaming",
                "Squads, clips, game chat, and voice lobbies.",
                "gaming",
                new[]
                {
                    "Pick a game channel",
                    "Share your tag in general",
                    "Join a squad voice room"
                },
                new[]
                {
                    new ServerTemplateCategoryDefinition(
                        "Lobby",
                        new[]
                        {
                            new ServerTemplateChannelDefinition("announcements", "text"),
                            new ServerTemplateChannelDefinition("general", "text"),
                            new ServerTemplateChannelDefinition("looking-for-group", "text"),
                            new ServerTemplateChannelDefinition("clips", "text")
                        }),
                    new ServerTemplateCategoryDefinition(
                        "Voice",
                        new[]
                        {
                            new ServerTemplateChannelDefinition("Lobby", "voice"),
                            new ServerTemplateChannelDefinition("Squad 1", "voice"),
                            new ServerTemplateChannelDefinition("Squad 2", "voice"),
                            new ServerTemplateChannelDefinition("AFK", "voice")
                        })
                }),
            new(
                "study",
                "Study Group",
                "Focused rooms for classes, resources, and quiet study.",
                "education",
                new[]
                {
                    "Post your subjects",
                    "Read shared resources",
                    "Join a study room"
                },
                new[]
                {
                    new ServerTemplateCategoryDefinition(
                        "Study Rooms",
                        new[]
                        {
                            new ServerTemplateChannelDefinition("general", "text"),
                            new ServerTemplateChannelDefinition("homework-help", "text"),
                            new ServerTemplateChannelDefinition("resources", "text")
                        }),
                    new ServerTemplateCategoryDefinition(
                        "Voice Rooms",
                        new[]
                        {
                            new ServerTemplateChannelDefinition("Study Room 1", "voice"),
                            new ServerTemplateChannelDefinition("Study Room 2", "voice"),
                            new ServerTemplateChannelDefinition("Quiet Focus", "voice")
                        })
                })
        };

        public ServerController(
            ApiContext context,
            IConfiguration config,
            IHubContext<ChatHub> hubContext,
            IMemoryCache? cache = null,
            IInviteAbuseDetectionService? inviteAbuseDetectionService = null)
        {
            _context = context;
            _config = config;
            _hubContext = hubContext;
            _cache = cache;
            _inviteAbuseDetectionService = inviteAbuseDetectionService;
            _publicDiscoveryCacheDuration = TimeSpan.FromSeconds(Math.Clamp(
                config.GetValue<int?>("Caching:PublicServerDiscoverySeconds") ?? 30,
                0,
                300));
        }

        private static object BuildServerResponse(
            CreateServer server,
            string role,
            bool alreadyMember = false,
            DateTime? onboardingCompletedAt = null)
        {
            return new
            {
                server.ServerID,
                server.ServerName,
                server.ServerOwner,
                server.InviteLink,
                server.Date,
                server.Description,
                server.ServerIconUrl,
                server.ServerBannerUrl,
                server.IsPublic,
                server.DiscoveryCategory,
                DiscoveryTags = DeserializeDiscoveryTags(server.DiscoveryTagsJson),
                server.WelcomeEnabled,
                server.WelcomeMessage,
                WelcomeChecklist = DeserializeWelcomeChecklist(server.WelcomeChecklistJson),
                VerificationLevel = ServerVerificationPolicy.NormalizeLevel(server.VerificationLevel),
                RequireVerifiedEmail = server.RequireVerifiedEmail,
                MinimumAccountAgeMinutes = ServerVerificationPolicy.NormalizeRequiredMinutes(server.MinimumAccountAgeMinutes),
                MinimumMembershipMinutes = ServerVerificationPolicy.NormalizeRequiredMinutes(server.MinimumMembershipMinutes),
                RequireTwoFactorForModerators = server.RequireTwoFactorForModerators,
                Role = role,
                AlreadyMember = alreadyMember,
                OnboardingCompletedAt = onboardingCompletedAt
            };
        }

        private static object BuildPublicServerListingResponse(
            CreateServer server,
            int memberCount,
            int channelCount,
            bool alreadyMember)
        {
            return new
            {
                server.ServerID,
                server.ServerName,
                server.ServerOwner,
                server.Description,
                server.ServerIconUrl,
                server.ServerBannerUrl,
                server.IsPublic,
                server.DiscoveryCategory,
                DiscoveryTags = DeserializeDiscoveryTags(server.DiscoveryTagsJson),
                server.Date,
                MemberCount = memberCount,
                ChannelCount = channelCount,
                AlreadyMember = alreadyMember,
                VerificationLevel = ServerVerificationPolicy.NormalizeLevel(server.VerificationLevel),
                RequireVerifiedEmail = server.RequireVerifiedEmail
            };
        }

        private static object BuildAutoModRuleResponse(ServerAutoModRule rule)
        {
            return new
            {
                rule.Id,
                rule.ServerId,
                rule.Name,
                TriggerType = AutoModService.NormalizeTriggerType(rule.TriggerType),
                rule.TriggerValue,
                ActionType = AutoModService.NormalizeActionType(rule.ActionType),
                rule.IsEnabled,
                rule.CreatedBy,
                rule.CreatedAt,
                rule.UpdatedAt,
                rule.TimesTriggered,
                rule.LastTriggeredAt
            };
        }

        private static bool TryNormalizeAutoModRule(
            AutoModRuleRequest request,
            out NormalizedAutoModRule normalized,
            out string error)
        {
            normalized = new NormalizedAutoModRule(
                string.Empty,
                AutoModService.TriggerKeyword,
                string.Empty,
                AutoModService.ActionBlockMessage,
                true);
            error = string.Empty;

            var name = request.Name?.Trim() ?? string.Empty;
            if (name.Length is < 1 or > 80)
            {
                error = "AutoMod rule name must be 1-80 characters.";
                return false;
            }

            if (!AutoModService.IsKnownTriggerType(request.TriggerType))
            {
                error = "AutoMod trigger must be keyword, invite_link, mention_spam, or link.";
                return false;
            }

            if (!AutoModService.IsKnownActionType(request.ActionType))
            {
                error = "AutoMod action must be block_message or flag.";
                return false;
            }

            var triggerType = AutoModService.NormalizeTriggerType(request.TriggerType);
            var triggerValue = (request.TriggerValue ?? string.Empty).Trim();
            if (triggerValue.Length > 1000)
            {
                error = "AutoMod trigger value must be 1000 characters or fewer.";
                return false;
            }

            if (triggerType == AutoModService.TriggerKeyword && !HasAutoModKeywordTerm(triggerValue))
            {
                error = "Keyword rules need at least one keyword.";
                return false;
            }

            if (triggerType == AutoModService.TriggerMentionSpam &&
                (!int.TryParse(triggerValue, out var mentionLimit) || mentionLimit is < 1 or > 100))
            {
                error = "Mention spam rules need a threshold between 1 and 100.";
                return false;
            }

            if (triggerType == AutoModService.TriggerInviteLink ||
                triggerType == AutoModService.TriggerLink)
            {
                triggerValue = string.Empty;
            }

            normalized = new NormalizedAutoModRule(
                name,
                triggerType,
                triggerValue,
                AutoModService.NormalizeActionType(request.ActionType),
                request.IsEnabled);
            return true;
        }

        private static bool HasAutoModKeywordTerm(string value)
        {
            return value
                .Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(term => term.Length > 0);
        }

        private static object BuildServerTemplateResponse(ServerTemplateDefinition template)
        {
            return new
            {
                template.Id,
                template.Name,
                template.Description,
                template.DiscoveryCategory,
                template.WelcomeChecklist,
                Categories = template.Categories.Select(category => new
                {
                    category.Name,
                    Channels = category.Channels.Select(channel => new
                    {
                        channel.Name,
                        Type = NormalizeChannelType(channel.Type) ?? "text"
                    })
                })
            };
        }

        private static ServerTemplateDefinition GetServerTemplate(string? templateId)
        {
            var normalizedTemplateId = templateId?.Trim();
            return ServerTemplates.FirstOrDefault(template =>
                       string.Equals(template.Id, normalizedTemplateId, StringComparison.OrdinalIgnoreCase)) ??
                   ServerTemplates[0];
        }

        private static string GetDefaultRoleColor(string? name)
        {
            return NormalizeRoleName(name) switch
            {
                "owner" => "#f0b232",
                "admin" => "#ed4245",
                "moderator" => "#23a559",
                "user" => "#5865f2",
                _ => "#949ba4"
            };
        }

        private static ServerRole BuildDefaultRole(string serverId, string name, int position)
        {
            var normalizedName = NormalizeRoleName(name);
            return new ServerRole
            {
                Id = Guid.NewGuid().ToString(),
                ServerId = serverId,
                Name = normalizedName,
                Color = GetDefaultRoleColor(normalizedName),
                Position = position,
                CanManageServer = normalizedName == "owner" || normalizedName == "admin",
                CanManageChannels = normalizedName is "owner" or "admin" or "moderator",
                CanManageMembers = normalizedName is "owner" or "admin" or "moderator",
                CanBanMembers = normalizedName is "owner" or "admin" or "moderator",
                CanCreateInvites = true,
                CanSendMessages = true,
                CanJoinVoice = true
            };
        }

        private static string BuildInviteLink(HttpRequest request, string code)
        {
            var scheme = request.IsHttps ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
            var host = request.Host.HasValue ? request.Host.Value : "localhost:5018";
            return $"{scheme}://{host}/invite/{code}";
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
            if (string.IsNullOrWhiteSpace(serverId) ||
                string.IsNullOrWhiteSpace(actionType) ||
                string.IsNullOrWhiteSpace(actorUsername))
            {
                return;
            }

            _context.ServerAuditLogs.Add(new ServerAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                ServerId = serverId,
                ActionType = actionType,
                ActorUsername = actorUsername,
                TargetType = NormalizeOptionalAuditValue(targetType),
                TargetId = NormalizeOptionalAuditValue(targetId),
                TargetUsername = NormalizeOptionalAuditValue(targetUsername),
                DetailsJson = BuildAuditDetailsJson(details),
                CreatedAt = DateTime.UtcNow
            });
        }

        private static object BuildAuditLogResponse(ServerAuditLog log)
        {
            return new
            {
                log.Id,
                log.ServerId,
                log.ActionType,
                log.ActorUsername,
                log.TargetType,
                log.TargetId,
                log.TargetUsername,
                log.DetailsJson,
                log.CreatedAt
            };
        }

        private static bool IsMemberMuted(ServerMember member, DateTime now)
        {
            return member.IsMuted && (member.MutedUntil == null || member.MutedUntil > now);
        }

        private static DateTime? GetActiveMuteUntil(IEnumerable<ServerMember> members, DateTime now)
        {
            var activeMutes = members
                .Where(member => IsMemberMuted(member, now))
                .ToList();

            if (!activeMutes.Any())
            {
                return null;
            }

            if (activeMutes.Any(member => member.MutedUntil == null))
            {
                return null;
            }

            return activeMutes
                .Select(member => member.MutedUntil)
                .Max();
        }

        private static DateTime? GetActiveTimeoutUntil(IEnumerable<ServerMember> members, DateTime now)
        {
            return members
                .Select(member => member.TimedOutUntil)
                .Where(until => until > now)
                .OrderByDescending(until => until)
                .FirstOrDefault();
        }

        private sealed class ServerMemberResponse
        {
            public string Id { get; set; } = string.Empty;
            public string Username { get; set; } = string.Empty;
            public string Role { get; set; } = "user";
            public string? ProfilePictureUrl { get; set; }
            public string PresenceStatus { get; set; } = "online";
            public string CustomStatus { get; set; } = string.Empty;
            public string ActivityStatus { get; set; } = string.Empty;
            public DateTime? LastActiveAt { get; set; }
            public bool ShowActivity { get; set; } = true;
            public string[] Badges { get; set; } = Array.Empty<string>();
            public bool IsMuted { get; set; }
            public DateTime? MutedUntil { get; set; }
            public bool IsTimedOut { get; set; }
            public DateTime? TimedOutUntil { get; set; }
            public bool IsBot { get; set; }
            public string? BotAccountId { get; set; }
            public bool IsEnabled { get; set; } = true;
        }

        private static ServerMemberResponse BuildMemberResponse(ServerMember member, string username, string role, DateTime now)
        {
            var entries = new[] { member };
            var isMuted = entries.Any(entry => IsMemberMuted(entry, now));
            var timeoutUntil = GetActiveTimeoutUntil(entries, now);

            return new ServerMemberResponse
            {
                Id = member.Id,
                Username = username,
                Role = role,
                IsMuted = isMuted,
                MutedUntil = isMuted ? GetActiveMuteUntil(entries, now) : null,
                IsTimedOut = timeoutUntil != null,
                TimedOutUntil = timeoutUntil
            };
        }

        private static ServerMemberResponse BuildMemberResponse(IGrouping<string, ServerMember> group, DateTime now)
        {
            var entries = group.ToList();
            var ownerEntry = entries.FirstOrDefault(member => member.Role == "owner");
            var chosenEntry = ownerEntry ?? entries.OrderBy(member => member.Id).First();
            var isMuted = entries.Any(member => IsMemberMuted(member, now));
            var timeoutUntil = GetActiveTimeoutUntil(entries, now);

            return new ServerMemberResponse
            {
                Id = chosenEntry.Id,
                Username = group.Key,
                Role = ownerEntry != null ? "owner" : chosenEntry.Role,
                IsMuted = isMuted,
                MutedUntil = isMuted ? GetActiveMuteUntil(entries, now) : null,
                IsTimedOut = timeoutUntil != null,
                TimedOutUntil = timeoutUntil
            };
        }

        private static ServerMemberResponse BuildBotMemberResponse(BotAccount bot)
        {
            return new ServerMemberResponse
            {
                Id = bot.Id,
                Username = string.IsNullOrWhiteSpace(bot.DisplayName) ? bot.Username : bot.DisplayName,
                Role = NormalizeRoleName(bot.Role),
                ProfilePictureUrl = bot.AvatarUrl,
                PresenceStatus = bot.IsEnabled ? "online" : "offline",
                CustomStatus = bot.IsEnabled ? "Bot account" : "Bot disabled",
                ActivityStatus = "Bot account",
                ShowActivity = true,
                Badges = Array.Empty<string>(),
                IsBot = true,
                BotAccountId = bot.Id,
                IsEnabled = bot.IsEnabled
            };
        }

        private static void ApplyMemberAccountData(ServerMemberResponse member, Account? account)
        {
            if (account == null)
            {
                return;
            }

            member.ProfilePictureUrl = account.ProfilePictureUrl;
            member.PresenceStatus = GetPublicPresenceStatus(account);
            member.ShowActivity = account.PrivacyShowActivity;
            member.CustomStatus = account.PrivacyShowActivity ? GetCustomStatus(account) : string.Empty;
            member.ActivityStatus = account.PrivacyShowActivity ? NormalizeActivityStatus(account.ActivityStatus) : string.Empty;
            member.LastActiveAt = account.PrivacyShowActivity ? account.LastActiveAt : null;
            member.Badges = GetProfileBadges(account);
        }

        private static string GetPublicPresenceStatus(Account account)
        {
            var normalized = NormalizePresenceStatus(account.PresenceStatus);
            return normalized == "invisible" ? "offline" : normalized;
        }

        private static string NormalizePresenceStatus(string? status)
        {
            var normalized = status?.Trim().ToLowerInvariant();
            return normalized is "online" or "idle" or "do-not-disturb" or "invisible"
                ? normalized
                : "online";
        }

        private static string NormalizeActivityStatus(string? activityStatus)
        {
            var normalized = (activityStatus ?? string.Empty).Trim();
            return normalized.Length <= 120 ? normalized : normalized[..120];
        }

        private static string GetCustomStatus(Account account)
        {
            if (string.IsNullOrWhiteSpace(account.SettingsJson))
            {
                return string.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(account.SettingsJson);
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("customStatus", out var customStatus) &&
                    customStatus.ValueKind == JsonValueKind.String)
                {
                    return customStatus.GetString()?.Trim() ?? string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }

            return string.Empty;
        }

        private static string[] GetProfileBadges(Account account)
        {
            if (string.IsNullOrWhiteSpace(account.SettingsJson))
            {
                return Array.Empty<string>();
            }

            try
            {
                using var document = JsonDocument.Parse(account.SettingsJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("profileBadges", out var badges) ||
                    badges.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<string>();
                }

                return badges
                    .EnumerateArray()
                    .Where(badge => badge.ValueKind == JsonValueKind.String)
                    .Select(badge => badge.GetString()?.Trim().ToLowerInvariant() ?? string.Empty)
                    .Where(badge => ProfileBadgeIds.Contains(badge))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(MaxProfileBadgeCount)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static bool TryGetModerationUntil(
            int? durationMinutes,
            bool allowIndefinite,
            out DateTime? until,
            out string? message)
        {
            until = null;
            message = null;

            if (durationMinutes == null)
            {
                if (allowIndefinite)
                {
                    return true;
                }

                message = "Duration is required.";
                return false;
            }

            if (durationMinutes is < 1 or > MaxModerationDurationMinutes)
            {
                message = $"Duration must be between 1 minute and {MaxModerationDurationMinutes} minutes.";
                return false;
            }

            until = DateTime.UtcNow.AddMinutes(durationMinutes.Value);
            return true;
        }

        private static string? BuildAuditDetailsJson(object? details)
        {
            if (details == null)
            {
                return null;
            }

            return JsonSerializer.Serialize(details);
        }

        private static string? NormalizeOptionalAuditValue(string? value)
        {
            var normalized = value?.Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private static DateTime? ParseAuditBoundary(string? value, bool endOfDay)
        {
            if (string.IsNullOrWhiteSpace(value) || !DateTime.TryParse(value, out var parsed))
            {
                return null;
            }

            var hasExplicitTime = value.Contains(':', StringComparison.Ordinal) ||
                                  value.Contains('T', StringComparison.OrdinalIgnoreCase);
            if (!hasExplicitTime)
            {
                parsed = endOfDay
                    ? parsed.Date.AddDays(1).AddTicks(-1)
                    : parsed.Date;
            }

            return parsed;
        }

        private static bool AuditTextContains(string? source, string query)
        {
            return !string.IsNullOrWhiteSpace(source) &&
                   source.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesAuditLogSearch(ServerAuditLog log, string query)
        {
            return string.IsNullOrWhiteSpace(query) ||
                   AuditTextContains(log.ActionType, query) ||
                   AuditTextContains(log.ActorUsername, query) ||
                   AuditTextContains(log.TargetType, query) ||
                   AuditTextContains(log.TargetId, query) ||
                   AuditTextContains(log.TargetUsername, query) ||
                   AuditTextContains(log.DetailsJson, query);
        }


        [HttpGet]
        public IActionResult GetServerTemplates()
        {
            return Ok(ServerTemplates.Select(BuildServerTemplateResponse));
        }

        [HttpPost]
        public async Task<IActionResult> CreateServer([FromBody] CreateServer createServer)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
            {
                return Unauthorized(new { Message = "Missing user identity." });
            }

            createServer.ServerName = createServer.ServerName?.Trim() ?? string.Empty;
            createServer.ServerOwner = currentUsername;
            createServer.ServerIconUrl = NormalizeOptionalMediaUrl(createServer.ServerIconUrl);
            createServer.ServerBannerUrl = NormalizeOptionalMediaUrl(createServer.ServerBannerUrl);

            Console.WriteLine($"making new server: '{createServer.ServerName}' by {createServer.ServerOwner}");

            if (!IsValidServerName(createServer.ServerName))
                return BadRequest(new { Message = "Server name must be 1-100 characters." });
            if (!IsValidServerMediaUrl(createServer.ServerIconUrl) || !IsValidServerMediaUrl(createServer.ServerBannerUrl))
                return BadRequest(new { Message = "Server icon and banner must be blank, an http URL, or an uploaded file URL." });

            var template = GetServerTemplate(createServer.TemplateId);
            createServer.ServerID = Guid.NewGuid().ToString();
            createServer.Date = DateTime.UtcNow;
            createServer.Description = NormalizeOptionalDescription(createServer.Description, 240) ?? template.Description;
            createServer.VerificationLevel = ServerVerificationPolicy.None;
            createServer.RequireVerifiedEmail = false;
            createServer.MinimumAccountAgeMinutes = 0;
            createServer.MinimumMembershipMinutes = 0;
            createServer.RequireTwoFactorForModerators = false;
            createServer.IsPublic = false;
            createServer.DiscoveryCategory = NormalizeDiscoveryCategory(template.DiscoveryCategory);
            createServer.DiscoveryTagsJson = SerializeDiscoveryTags(Array.Empty<string>());
            createServer.WelcomeEnabled = true;
            createServer.WelcomeMessage = null;
            createServer.WelcomeChecklistJson = SerializeWelcomeChecklist(template.WelcomeChecklist);
            var defaultInviteCode = GenerateInviteCode();
            createServer.InviteLink = BuildInviteLink(Request, defaultInviteCode);

            _context.CreateServers.Add(createServer);
            await _context.SaveChangesAsync();

            _context.ServerInvites.Add(new ServerInvite
            {
                Id = Guid.NewGuid().ToString(),
                ServerId = createServer.ServerID,
                Code = defaultInviteCode,
                CreatedBy = createServer.ServerOwner,
                CreatedAt = DateTime.UtcNow
            });

            _context.ServerRoles.AddRange(
                BuildDefaultRole(createServer.ServerID, "owner", 0),
                BuildDefaultRole(createServer.ServerID, "admin", 1),
                BuildDefaultRole(createServer.ServerID, "moderator", 2),
                BuildDefaultRole(createServer.ServerID, "user", 3)
            );

            var ownerMembership = new ServerMember
            {
                Id = Guid.NewGuid().ToString(),
                ServerId = createServer.ServerID,
                Username = createServer.ServerOwner,
                Role = "owner",
                JoinedAt = DateTime.UtcNow
            };

            _context.ServerMembers.Add(ownerMembership);
            await _context.SaveChangesAsync();

            var categories = new List<Category>();
            var channels = new List<Channel>();
            foreach (var (templateCategory, categoryIndex) in template.Categories.Select((value, index) => (value, index)))
            {
                var category = new Category
                {
                    Id = Guid.NewGuid().ToString(),
                    ServerId = createServer.ServerID,
                    Name = templateCategory.Name,
                    Position = categoryIndex
                };
                categories.Add(category);

                foreach (var (templateChannel, channelIndex) in templateCategory.Channels.Select((value, index) => (value, index)))
                {
                    channels.Add(new Channel
                    {
                        Id = Guid.NewGuid().ToString(),
                        ServerId = createServer.ServerID,
                        CategoryId = category.Id,
                        Name = NormalizeName(templateChannel.Name),
                        Type = NormalizeChannelType(templateChannel.Type) ?? "text",
                        Position = channelIndex
                    });
                }
            }

            _context.Categories.AddRange(categories);
            _context.Channels.AddRange(channels);
            AddAuditLog(
                createServer.ServerID,
                "server_created",
                currentUsername,
                "server",
                createServer.ServerID,
                details: new { createServer.ServerName, TemplateId = template.Id });
            await _context.SaveChangesAsync();

            return Ok(BuildServerResponse(createServer, "owner"));
        }


        [HttpGet]
        public async Task<IActionResult> GetServer(string? username = null)
        {
            var normalizedUsername = GetCurrentUsername();
            if (string.IsNullOrWhiteSpace(normalizedUsername))
            {
                return Unauthorized(new { Message = "Missing user identity." });
            }

            var memberships = await _context.ServerMembers
                .Where(member => member.Username == normalizedUsername)
                .ToListAsync();

            var memberServerIds = memberships
                .Select(member => member.ServerId)
                .Distinct()
                .ToList();

            var servers = await _context.CreateServers
                .Where(server => memberServerIds.Contains(server.ServerID ?? "") || server.ServerOwner == normalizedUsername)
                .ToListAsync();

            var membershipsByServerId = memberships
                .GroupBy(member => member.ServerId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Any(member => member.Role == "owner")
                        ? "owner"
                        : group.Select(member => member.Role).FirstOrDefault() ?? "user"
                );
            var onboardingByServerId = memberships
                .GroupBy(member => member.ServerId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(member => member.OnboardingCompletedAt)
                        .Where(completedAt => completedAt != null)
                        .OrderByDescending(completedAt => completedAt)
                        .FirstOrDefault()
                );

            var serverResponse = servers
                .Select(server =>
                {
                    var serverId = server.ServerID ?? string.Empty;
                    var role = membershipsByServerId.TryGetValue(serverId, out var membershipRole)
                        ? membershipRole
                        : server.ServerOwner == normalizedUsername
                            ? "owner"
                            : "user";

                    onboardingByServerId.TryGetValue(serverId, out var onboardingCompletedAt);
                    return BuildServerResponse(server, role, onboardingCompletedAt: onboardingCompletedAt);
                })
                .ToList();

            if (serverResponse.Any())
            {
                return new JsonResult(
                    serverResponse,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
                );
            }
            else
            {
                return new JsonResult(
                    Array.Empty<object>(),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
                );
            }
        }


        [HttpGet]
        public async Task<IActionResult> DiscoverServers(string? query = null, string? category = null, string? tag = null, int take = 24)
        {
            var currentUsername = GetCurrentUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
            {
                return Unauthorized(new { Message = "Missing user identity." });
            }

            take = Math.Clamp(take, 1, 50);
            var normalizedQuery = query?.Trim();
            var normalizedCategory = NormalizeDiscoveryCategory(category);
            var normalizedTag = NormalizeDiscoveryTag(tag);
            var normalizedQueryTag = NormalizeDiscoveryTag(normalizedQuery);

            var discovery = await GetPublicServerDiscoveryAsync(
                normalizedQuery,
                normalizedCategory,
                normalizedTag,
                normalizedQueryTag,
                take);

            var serverIds = discovery.Servers
                .Select(server => server.ServerID)
                .Where(serverId => !string.IsNullOrWhiteSpace(serverId))
                .Cast<string>()
                .ToList();

            var joinedServerIds = await _context.ServerMembers
                .Where(member => member.Username == currentUsername && serverIds.Contains(member.ServerId))
                .Select(member => member.ServerId)
                .Distinct()
                .ToListAsync();
            var joined = joinedServerIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

            return Ok(discovery.Servers.Select(server =>
            {
                var serverId = server.ServerID ?? string.Empty;
                return BuildPublicServerListingResponse(
                    server,
                    discovery.MemberCounts.GetValueOrDefault(serverId),
                    discovery.ChannelCounts.GetValueOrDefault(serverId),
                    joined.Contains(serverId) || server.ServerOwner == currentUsername);
            }));
        }

        private async Task<PublicServerDiscoveryCacheItem> GetPublicServerDiscoveryAsync(
            string? normalizedQuery,
            string? normalizedCategory,
            string? normalizedTag,
            string? normalizedQueryTag,
            int take)
        {
            var cacheKey = BuildPublicServerDiscoveryCacheKey(
                normalizedQuery,
                normalizedCategory,
                normalizedTag,
                take);
            if (_publicDiscoveryCacheDuration > TimeSpan.Zero &&
                _cache != null &&
                _cache.TryGetValue<PublicServerDiscoveryCacheItem>(cacheKey, out var cachedDiscovery) &&
                cachedDiscovery != null)
            {
                return cachedDiscovery;
            }

            var publicServersQuery = _context.CreateServers
                .AsNoTracking()
                .Where(server => server.IsPublic);

            if (!string.IsNullOrWhiteSpace(normalizedCategory))
            {
                publicServersQuery = publicServersQuery.Where(server => server.DiscoveryCategory == normalizedCategory);
            }

            if (!string.IsNullOrWhiteSpace(normalizedTag))
            {
                publicServersQuery = publicServersQuery.Where(server =>
                    server.DiscoveryTagsJson != null &&
                    server.DiscoveryTagsJson.Contains(normalizedTag));
            }

            if (!string.IsNullOrWhiteSpace(normalizedQuery))
            {
                publicServersQuery = publicServersQuery.Where(server =>
                    server.ServerName.Contains(normalizedQuery) ||
                    (server.Description != null && server.Description.Contains(normalizedQuery)) ||
                    (server.DiscoveryCategory != null && server.DiscoveryCategory.Contains(normalizedQuery)) ||
                    (server.DiscoveryTagsJson != null &&
                        (server.DiscoveryTagsJson.Contains(normalizedQuery) ||
                         (normalizedQueryTag != null && server.DiscoveryTagsJson.Contains(normalizedQueryTag)))));
            }

            var publicServers = await publicServersQuery
                .OrderByDescending(server => _context.ServerMembers.Count(member => member.ServerId == server.ServerID))
                .ThenBy(server => server.ServerName)
                .Take(take)
                .ToListAsync();

            var serverIds = publicServers
                .Select(server => server.ServerID)
                .Where(serverId => !string.IsNullOrWhiteSpace(serverId))
                .Cast<string>()
                .ToList();

            var memberCounts = await _context.ServerMembers
                .AsNoTracking()
                .Where(member => serverIds.Contains(member.ServerId))
                .GroupBy(member => member.ServerId)
                .Select(group => new { ServerId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.ServerId, item => item.Count);

            var channelCounts = await _context.Channels
                .AsNoTracking()
                .Where(channel => serverIds.Contains(channel.ServerId))
                .GroupBy(channel => channel.ServerId)
                .Select(group => new { ServerId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.ServerId, item => item.Count);

            var discovery = new PublicServerDiscoveryCacheItem(publicServers, memberCounts, channelCounts);
            if (_publicDiscoveryCacheDuration > TimeSpan.Zero && _cache != null)
            {
                _cache.Set(
                    cacheKey,
                    discovery,
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = _publicDiscoveryCacheDuration
                    });
            }

            return discovery;
        }

        private static string BuildPublicServerDiscoveryCacheKey(
            string? normalizedQuery,
            string? normalizedCategory,
            string? normalizedTag,
            int take)
        {
            var version = System.Threading.Volatile.Read(ref _publicDiscoveryCacheVersion);
            return string.Join(
                ':',
                "public-server-discovery",
                version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                take.ToString(System.Globalization.CultureInfo.InvariantCulture),
                normalizedCategory ?? string.Empty,
                normalizedTag ?? string.Empty,
                normalizedQuery?.ToLowerInvariant() ?? string.Empty);
        }

        private static void InvalidatePublicServerDiscoveryCache()
        {
            System.Threading.Interlocked.Increment(ref _publicDiscoveryCacheVersion);
        }

        [HttpGet]
        public Task<IActionResult> PublicServers(string? query = null, string? category = null, string? tag = null, int take = 24)
        {
            return DiscoverServers(query, category, tag, take);
        }

        [HttpPost]
        public async Task<IActionResult> UpdatePublicListing([FromBody] PublicServerListingRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageServer(request.ServerId, currentUsername))
                return Forbid();

            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == request.ServerId);
            if (server == null)
                return NotFound(new { Message = "Server not found." });

            var description = NormalizeOptionalDescription(request.Description, 240);
            var category = NormalizeDiscoveryCategory(request.DiscoveryCategory);
            var discoveryTags = NormalizeDiscoveryTags(request.DiscoveryTags);
            if (request.IsPublic && string.IsNullOrWhiteSpace(description))
                return BadRequest(new { Message = "Public listings need a short description." });

            server.IsPublic = request.IsPublic;
            server.Description = description;
            server.DiscoveryCategory = category;
            server.DiscoveryTagsJson = SerializeDiscoveryTags(discoveryTags);

            AddAuditLog(
                request.ServerId,
                "public_listing_updated",
                currentUsername,
                "server",
                request.ServerId,
                details: new
                {
                    server.IsPublic,
                    server.Description,
                    server.DiscoveryCategory,
                    DiscoveryTags = discoveryTags
                });
            await _context.SaveChangesAsync();
            InvalidatePublicServerDiscoveryCache();

            return Ok(BuildServerResponse(
                server,
                server.ServerOwner == currentUsername
                    ? "owner"
                    : await _context.ServerMembers
                        .Where(member => member.ServerId == request.ServerId && member.Username == currentUsername)
                        .Select(member => member.Role)
                        .FirstOrDefaultAsync() ?? "user"));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateServerAppearance([FromBody] ServerAppearanceRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageServer(request.ServerId, currentUsername))
                return Forbid();

            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == request.ServerId);
            if (server == null)
                return NotFound(new { Message = "Server not found." });

            var iconUrl = NormalizeOptionalMediaUrl(request.ServerIconUrl);
            var bannerUrl = NormalizeOptionalMediaUrl(request.ServerBannerUrl);
            if (!IsValidServerMediaUrl(iconUrl) || !IsValidServerMediaUrl(bannerUrl))
                return BadRequest(new { Message = "Server icon and banner must be blank, an http URL, or an uploaded file URL." });

            server.ServerIconUrl = iconUrl;
            server.ServerBannerUrl = bannerUrl;

            AddAuditLog(
                request.ServerId,
                "server_appearance_updated",
                currentUsername,
                "server",
                request.ServerId,
                details: new
                {
                    server.ServerIconUrl,
                    server.ServerBannerUrl
                });
            await _context.SaveChangesAsync();

            return Ok(BuildServerResponse(
                server,
                server.ServerOwner == currentUsername
                    ? "owner"
                    : await _context.ServerMembers
                        .Where(member => member.ServerId == request.ServerId && member.Username == currentUsername)
                        .Select(member => member.Role)
                        .FirstOrDefaultAsync() ?? "user"));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateWelcomeScreen([FromBody] ServerWelcomeRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageServer(request.ServerId, currentUsername))
                return Forbid();

            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == request.ServerId);
            if (server == null)
                return NotFound(new { Message = "Server not found." });

            var welcomeMessage = NormalizeOptionalDescription(request.WelcomeMessage, MaxWelcomeMessageLength);
            var checklist = NormalizeWelcomeChecklist(request.WelcomeChecklist);

            server.WelcomeEnabled = request.WelcomeEnabled;
            server.WelcomeMessage = welcomeMessage;
            server.WelcomeChecklistJson = SerializeWelcomeChecklist(checklist);

            AddAuditLog(
                request.ServerId,
                "welcome_screen_updated",
                currentUsername,
                "server",
                request.ServerId,
                details: new
                {
                    server.WelcomeEnabled,
                    server.WelcomeMessage,
                    WelcomeChecklist = checklist
                });
            await _context.SaveChangesAsync();

            return Ok(BuildServerResponse(
                server,
                server.ServerOwner == currentUsername
                    ? "owner"
                    : await _context.ServerMembers
                        .Where(member => member.ServerId == request.ServerId && member.Username == currentUsername)
                        .Select(member => member.Role)
                        .FirstOrDefaultAsync() ?? "user"));
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> JoinPublicServer([FromBody] ServerActionRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == request.ServerId);
            if (server == null || string.IsNullOrWhiteSpace(server.ServerID))
                return NotFound(new { Message = "Server not found." });

            if (!server.IsPublic)
                return BadRequest(new { Message = "This server is not listed publicly." });

            return await JoinServerCore(currentUsername, server);
        }


        [HttpGet]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> GetInviteLink(string serverId)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
            {
                return Unauthorized(new { Message = "Missing user identity." });
            }

            var server = await _context.CreateServers
                .FirstOrDefaultAsync(s => s.ServerID == serverId);

            if (server == null)
                return NotFound(new { Message = "Server not found" });

            if (!await CanCreateInvite(serverId, currentUsername))
            {
                return Forbid();
            }

            var invite = await _context.ServerInvites
                .Where(i => i.ServerId == serverId && i.RevokedAt == null &&
                            i.AbuseDetectedAt == null &&
                            (i.ExpiresAt == null || i.ExpiresAt > DateTime.UtcNow) &&
                            (i.MaxUses == null || i.Uses < i.MaxUses))
                .OrderBy(i => i.CreatedAt)
                .FirstOrDefaultAsync();

            if (invite == null)
            {
                invite = new ServerInvite
                {
                    Id = Guid.NewGuid().ToString(),
                    ServerId = serverId,
                    Code = GenerateInviteCode(),
                    CreatedBy = currentUsername,
                    CreatedAt = DateTime.UtcNow
                };
                _context.ServerInvites.Add(invite);
            }

            var link = BuildInviteLink(Request, invite.Code);
            if (string.IsNullOrEmpty(server.InviteLink) || server.InviteLink != link)
            {
                server.InviteLink = link;
                _context.CreateServers.Update(server);
            }

            await _context.SaveChangesAsync();

            return Ok(new { InviteLink = link, Code = invite.Code });
        }


        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> JoinServer([FromBody] JoinServerRequest req)
        {
            var normalizedUsername = GetCurrentUsername();
            var normalizedInviteLink = req.InviteLink?.Trim();

            if (string.IsNullOrWhiteSpace(normalizedUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            if (string.IsNullOrWhiteSpace(normalizedInviteLink))
                return BadRequest(new { Message = "Invite link is required." });

            var inviteCode = ExtractInviteCode(normalizedInviteLink);
            var invite = await _context.ServerInvites.FirstOrDefaultAsync(i => i.Code == inviteCode);
            CreateServer? server = null;

            if (invite != null)
            {
                if (invite.RevokedAt != null ||
                    invite.AbuseDetectedAt != null ||
                    invite.ExpiresAt <= DateTime.UtcNow ||
                    (invite.MaxUses != null && invite.Uses >= invite.MaxUses))
                {
                    return BadRequest(new { Message = "Invite link is expired, paused, or has reached its usage limit." });
                }

                server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == invite.ServerId);
            }

            server ??= await _context.CreateServers
                .FirstOrDefaultAsync(s => s.InviteLink == normalizedInviteLink);

            if (server == null)
                return NotFound(new { Message = "Server not found" });

            return await JoinServerCore(normalizedUsername, server, invite);
        }


        [HttpGet]
        public async Task<IActionResult> GetServerDetails(string serverId)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
            {
                return Unauthorized(new { Message = "Missing user identity." });
            }

            if (!await IsServerMember(serverId, currentUsername))
            {
                return Forbid();
            }

            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == serverId);
            if (server == null)
            {
                return NotFound(new { Message = "Server not found." });
            }

            var categories = await _context.Categories
                .Where(c => c.ServerId == serverId)
                .OrderBy(c => c.Position)
                .ThenBy(c => c.Name)
                .ToListAsync();
            var channels = await _context.Channels
                .Where(c => c.ServerId == serverId)
                .OrderBy(c => c.Position)
                .ThenBy(c => c.Name)
                .ToListAsync();
            var visibleChannels = new List<Channel>();
            foreach (var channel in channels)
            {
                if (await CanViewChannel(channel, currentUsername))
                {
                    visibleChannels.Add(channel);
                }
            }
            var membership = await _context.ServerMembers
                .Where(member => member.ServerId == serverId && member.Username == currentUsername)
                .OrderByDescending(member => member.Role == "owner")
                .ThenBy(member => member.Id)
                .FirstOrDefaultAsync();

            return Ok(new
            {
                Server = BuildServerResponse(
                    server,
                    server.ServerOwner == currentUsername
                        ? "owner"
                        : membership?.Role ?? "user",
                    onboardingCompletedAt: membership?.OnboardingCompletedAt),
                Categories = categories,
                Channels = visibleChannels
            });
        }

        [HttpPost]
        public async Task<IActionResult> CompleteOnboarding([FromBody] ServerActionRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            var membership = await _context.ServerMembers
                .Where(member => member.ServerId == request.ServerId && member.Username == currentUsername)
                .OrderByDescending(member => member.Role == "owner")
                .ThenBy(member => member.Id)
                .FirstOrDefaultAsync();

            if (membership == null)
                return Forbid();

            membership.OnboardingCompletedAt ??= DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                membership.ServerId,
                membership.Username,
                membership.OnboardingCompletedAt
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetServerMembers(string serverId)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
            {
                return Unauthorized(new { Message = "Missing user identity." });
            }

            if (!await IsServerMember(serverId, currentUsername))
            {
                return Forbid();
            }

            var members = await _context.ServerMembers
                .Where(m => m.ServerId == serverId)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var dedupedMembers = members
                .GroupBy(member => member.Username)
                .Select(group => BuildMemberResponse(group, now))
                .ToList();

            var bots = await _context.BotAccounts
                .Where(bot => bot.ServerId == serverId)
                .ToListAsync();
            dedupedMembers.AddRange(bots.Select(BuildBotMemberResponse));

            var usernames = dedupedMembers
                .Where(member => !member.IsBot)
                .Select(member => member.Username)
                .ToList();
            var accounts = await _context.Accounts
                .Where(account => usernames.Contains(account.UserName) && !account.IsDisabled)
                .ToDictionaryAsync(account => account.UserName, StringComparer.OrdinalIgnoreCase);

            foreach (var member in dedupedMembers)
            {
                accounts.TryGetValue(member.Username, out var account);
                ApplyMemberAccountData(member, account);
            }

            return Ok(dedupedMembers
                .OrderBy(member => member.Role == "owner" ? 0 : 1)
                .ThenBy(member => member.IsBot ? 1 : 0)
                .ThenBy(member => member.Username)
                .ToList());
        }

        [HttpGet]
        public async Task<IActionResult> GetAuditLogs(
            string serverId,
            int take = 50,
            string query = "",
            string? actionType = null,
            string? actor = null,
            string? target = null,
            string? after = null,
            string? before = null)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            var normalizedServerId = serverId?.Trim() ?? string.Empty;
            if (!await CanManageServer(normalizedServerId, currentUsername))
                return Forbid();

            query = query?.Trim() ?? string.Empty;
            actionType = NormalizeOptionalAuditValue(actionType);
            actor = NormalizeOptionalAuditValue(actor);
            target = NormalizeOptionalAuditValue(target);
            var afterDate = ParseAuditBoundary(after, endOfDay: false);
            var beforeDate = ParseAuditBoundary(before, endOfDay: true);
            take = Math.Clamp(take, 1, 100);
            var logs = await _context.ServerAuditLogs
                .Where(log => log.ServerId == normalizedServerId)
                .OrderByDescending(log => log.CreatedAt)
                .ToListAsync();

            var filteredLogs = logs
                .Where(log => MatchesAuditLogSearch(log, query))
                .Where(log => string.IsNullOrWhiteSpace(actionType) ||
                              log.ActionType.Contains(actionType, StringComparison.OrdinalIgnoreCase))
                .Where(log => string.IsNullOrWhiteSpace(actor) ||
                              log.ActorUsername.Contains(actor, StringComparison.OrdinalIgnoreCase))
                .Where(log => string.IsNullOrWhiteSpace(target) ||
                              AuditTextContains(log.TargetType, target) ||
                              AuditTextContains(log.TargetId, target) ||
                              AuditTextContains(log.TargetUsername, target))
                .Where(log => afterDate == null || log.CreatedAt >= afterDate.Value)
                .Where(log => beforeDate == null || log.CreatedAt <= beforeDate.Value)
                .Take(take)
                .ToList();

            return Ok(filteredLogs.Select(BuildAuditLogResponse));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateVerificationLevel([FromBody] ServerVerificationLevelRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageServer(request.ServerId, currentUsername))
                return Forbid();

            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == request.ServerId);
            if (server == null)
                return NotFound(new { Message = "Server not found." });

            if (!ServerVerificationPolicy.IsValidLevel(request.VerificationLevel))
                return BadRequest(new { Message = "Verification level must be none, low, medium, high, or highest." });

            if (!ServerVerificationPolicy.IsValidRequiredMinutes(request.MinimumAccountAgeMinutes) ||
                !ServerVerificationPolicy.IsValidRequiredMinutes(request.MinimumMembershipMinutes))
                return BadRequest(new { Message = "Rule minutes must be between 0 and 525600." });

            server.VerificationLevel = ServerVerificationPolicy.NormalizeLevel(request.VerificationLevel);
            if (request.RequireVerifiedEmail != null)
            {
                server.RequireVerifiedEmail = request.RequireVerifiedEmail.Value;
            }
            if (request.MinimumAccountAgeMinutes != null)
            {
                server.MinimumAccountAgeMinutes =
                    ServerVerificationPolicy.NormalizeRequiredMinutes(request.MinimumAccountAgeMinutes);
            }
            if (request.MinimumMembershipMinutes != null)
            {
                server.MinimumMembershipMinutes =
                    ServerVerificationPolicy.NormalizeRequiredMinutes(request.MinimumMembershipMinutes);
            }
            if (request.RequireTwoFactorForModerators != null)
            {
                server.RequireTwoFactorForModerators = request.RequireTwoFactorForModerators.Value;
            }
            AddAuditLog(
                request.ServerId,
                "server_rules_updated",
                currentUsername,
                "server",
                request.ServerId,
                details: new
                {
                    server.VerificationLevel,
                    server.RequireVerifiedEmail,
                    server.MinimumAccountAgeMinutes,
                    server.MinimumMembershipMinutes,
                    server.RequireTwoFactorForModerators
                });
            await _context.SaveChangesAsync();

            return Ok(BuildServerResponse(server, server.ServerOwner == currentUsername ? "owner" : "user"));
        }

        [HttpGet]
        public async Task<IActionResult> GetAutoModRules(string serverId)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageServer(serverId, currentUsername))
                return Forbid();

            var rules = await _context.ServerAutoModRules
                .Where(rule => rule.ServerId == serverId)
                .OrderByDescending(rule => rule.IsEnabled)
                .ThenBy(rule => rule.CreatedAt)
                .ToListAsync();

            return Ok(rules.Select(BuildAutoModRuleResponse));
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> CreateAutoModRule([FromBody] AutoModRuleRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageServer(request.ServerId, currentUsername))
                return Forbid();

            if (!await _context.CreateServers.AnyAsync(server => server.ServerID == request.ServerId))
                return NotFound(new { Message = "Server not found." });

            var ruleCount = await _context.ServerAutoModRules.CountAsync(rule => rule.ServerId == request.ServerId);
            if (ruleCount >= 25)
                return BadRequest(new { Message = "A server can have up to 25 AutoMod rules." });

            if (!TryNormalizeAutoModRule(request, out var normalized, out var error))
                return BadRequest(new { Message = error });

            var now = DateTime.UtcNow;
            var rule = new ServerAutoModRule
            {
                Id = Guid.NewGuid().ToString(),
                ServerId = request.ServerId,
                Name = normalized.Name,
                TriggerType = normalized.TriggerType,
                TriggerValue = normalized.TriggerValue,
                ActionType = normalized.ActionType,
                IsEnabled = normalized.IsEnabled,
                CreatedBy = currentUsername,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.ServerAutoModRules.Add(rule);
            AddAuditLog(
                request.ServerId,
                "automod_rule_created",
                currentUsername,
                "automod_rule",
                rule.Id,
                details: new { rule.Name, rule.TriggerType, rule.ActionType, rule.IsEnabled });
            await _context.SaveChangesAsync();

            return Ok(BuildAutoModRuleResponse(rule));
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> UpdateAutoModRule([FromBody] AutoModRuleRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            var rule = await _context.ServerAutoModRules.FirstOrDefaultAsync(item => item.Id == request.RuleId);
            if (rule == null)
                return NotFound(new { Message = "AutoMod rule not found." });

            if (!string.Equals(rule.ServerId, request.ServerId, StringComparison.Ordinal))
                return BadRequest(new { Message = "AutoMod rule does not belong to this server." });

            if (!await CanManageServer(rule.ServerId, currentUsername))
                return Forbid();

            if (!TryNormalizeAutoModRule(request, out var normalized, out var error))
                return BadRequest(new { Message = error });

            rule.Name = normalized.Name;
            rule.TriggerType = normalized.TriggerType;
            rule.TriggerValue = normalized.TriggerValue;
            rule.ActionType = normalized.ActionType;
            rule.IsEnabled = normalized.IsEnabled;
            rule.UpdatedAt = DateTime.UtcNow;

            AddAuditLog(
                rule.ServerId,
                "automod_rule_updated",
                currentUsername,
                "automod_rule",
                rule.Id,
                details: new { rule.Name, rule.TriggerType, rule.ActionType, rule.IsEnabled });
            await _context.SaveChangesAsync();

            return Ok(BuildAutoModRuleResponse(rule));
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> DeleteAutoModRule([FromBody] AutoModRuleActionRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            var rule = await _context.ServerAutoModRules.FirstOrDefaultAsync(item => item.Id == request.RuleId);
            if (rule == null)
                return NotFound(new { Message = "AutoMod rule not found." });

            if (!string.Equals(rule.ServerId, request.ServerId, StringComparison.Ordinal))
                return BadRequest(new { Message = "AutoMod rule does not belong to this server." });

            if (!await CanManageServer(rule.ServerId, currentUsername))
                return Forbid();

            _context.ServerAutoModRules.Remove(rule);
            AddAuditLog(
                rule.ServerId,
                "automod_rule_deleted",
                currentUsername,
                "automod_rule",
                rule.Id,
                details: new { rule.Name, rule.TriggerType, rule.ActionType });
            await _context.SaveChangesAsync();

            return Ok(new { Message = "AutoMod rule deleted." });
        }

        [HttpPost]
        public async Task<IActionResult> CreateChannel([FromBody] ChannelMutationRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageChannels(request.ServerId, currentUsername))
                return Forbid();

            var name = NormalizeName(request.Name);
            var type = NormalizeChannelType(request.Type);
            if (!await _context.CreateServers.AnyAsync(server => server.ServerID == request.ServerId))
                return NotFound(new { Message = "Server not found." });
            if (!IsValidChannelName(name))
                return BadRequest(new { Message = "Channel name must be 1-80 characters." });
            if (type == null)
                return BadRequest(new { Message = "Channel type must be text, voice, or stage." });
            if (!string.IsNullOrWhiteSpace(request.CategoryId) &&
                !await _context.Categories.AnyAsync(category => category.Id == request.CategoryId && category.ServerId == request.ServerId))
                return BadRequest(new { Message = "Category does not belong to this server." });

            var channel = new Channel
            {
                Id = Guid.NewGuid().ToString(),
                ServerId = request.ServerId,
                CategoryId = string.IsNullOrWhiteSpace(request.CategoryId) ? null : request.CategoryId,
                Name = name,
                Type = type,
                Position = await _context.Channels.CountAsync(channel =>
                    channel.ServerId == request.ServerId && channel.CategoryId == request.CategoryId)
            };

            _context.Channels.Add(channel);
            AddAuditLog(
                request.ServerId,
                "channel_created",
                currentUsername,
                "channel",
                channel.Id,
                details: new { channel.Name, channel.Type, channel.CategoryId });
            await _context.SaveChangesAsync();
            InvalidatePublicServerDiscoveryCache();
            return Ok(channel);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateChannel([FromBody] ChannelMutationRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            var channel = await _context.Channels.FirstOrDefaultAsync(c => c.Id == request.ChannelId);
            if (channel == null)
                return NotFound(new { Message = "Channel not found." });

            if (!await CanManageChannels(channel.ServerId, currentUsername))
                return Forbid();

            var name = NormalizeName(request.Name);
            var type = NormalizeChannelType(request.Type);
            if (!IsValidChannelName(name))
                return BadRequest(new { Message = "Channel name must be 1-80 characters." });
            if (type == null)
                return BadRequest(new { Message = "Channel type must be text, voice, or stage." });
            if (!string.IsNullOrWhiteSpace(request.CategoryId) &&
                !await _context.Categories.AnyAsync(category => category.Id == request.CategoryId && category.ServerId == channel.ServerId))
                return BadRequest(new { Message = "Category does not belong to this server." });

            var previous = new { channel.Name, channel.Type, channel.CategoryId };
            channel.Name = name;
            channel.Type = type;
            channel.CategoryId = string.IsNullOrWhiteSpace(request.CategoryId) ? null : request.CategoryId;

            AddAuditLog(
                channel.ServerId,
                "channel_updated",
                currentUsername,
                "channel",
                channel.Id,
                details: new
                {
                    Previous = previous,
                    Current = new { channel.Name, channel.Type, channel.CategoryId }
                });
            await _context.SaveChangesAsync();
            return Ok(channel);
        }

        [HttpGet]
        public async Task<IActionResult> GetChannelPermissions(string channelId)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            var channel = await _context.Channels.FirstOrDefaultAsync(c => c.Id == channelId);
            if (channel == null)
                return NotFound(new { Message = "Channel not found." });

            if (!await CanViewChannel(channel, currentUsername))
                return Forbid();

            await EnsureDefaultRoles(channel.ServerId);
            return Ok(await BuildChannelPermissionsResponse(channel));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateChannelPermissions([FromBody] ChannelPermissionsRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            var channel = await _context.Channels.FirstOrDefaultAsync(c => c.Id == request.ChannelId);
            if (channel == null)
                return NotFound(new { Message = "Channel not found." });

            if (!await CanManageChannels(channel.ServerId, currentUsername))
                return Forbid();

            await EnsureDefaultRoles(channel.ServerId);
            var validRoleNames = (await _context.ServerRoles
                    .Where(role => role.ServerId == channel.ServerId)
                    .Select(role => role.Name)
                    .ToListAsync())
                .Select(NormalizeRoleName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var viewRoles = NormalizeRequestedRoleNames(request.ViewAllowedRoleNames, validRoleNames);
            var sendRoles = NormalizeRequestedRoleNames(request.MessageSendAllowedRoleNames, validRoleNames);
            var voiceAllowedRoles = NormalizeRequestedRoleNames(request.VoiceAllowedRoleNames, validRoleNames);
            var stageSpeakerRoles = NormalizeRequestedRoleNames(request.StageSpeakerRoleNames, validRoleNames);

            if (request.ViewAccessRestricted && viewRoles.Length == 0)
            {
                return BadRequest(new { Message = "At least one role must be allowed to view this channel." });
            }

            if (channel.Type == "text" && request.MessageSendRestricted && sendRoles.Length == 0)
            {
                return BadRequest(new { Message = "At least one role must be allowed to send messages." });
            }

            if (IsVoiceLikeChannelType(channel.Type) && request.VoiceAccessRestricted && voiceAllowedRoles.Length == 0)
            {
                return BadRequest(new { Message = "At least one role must be allowed to connect." });
            }

            if (channel.Type == "stage" && request.StageSpeakerRestricted && stageSpeakerRoles.Length == 0)
            {
                return BadRequest(new { Message = "At least one role must be allowed to speak on stage." });
            }

            channel.ViewAccessRestricted = request.ViewAccessRestricted;
            channel.ViewAllowedRolesJson = request.ViewAccessRestricted
                ? SerializeRoleNames(viewRoles)
                : "[]";
            channel.MessageSendRestricted = channel.Type == "text" && request.MessageSendRestricted;
            channel.MessageSendAllowedRolesJson = channel.MessageSendRestricted
                ? SerializeRoleNames(sendRoles)
                : "[]";
            channel.VoiceAccessRestricted = IsVoiceLikeChannelType(channel.Type) && request.VoiceAccessRestricted;
            channel.VoiceAllowedRolesJson = channel.VoiceAccessRestricted
                ? SerializeRoleNames(voiceAllowedRoles)
                : "[]";
            channel.StageSpeakerRestricted = channel.Type == "stage" && request.StageSpeakerRestricted;
            channel.StageSpeakerRolesJson = channel.StageSpeakerRestricted
                ? SerializeRoleNames(stageSpeakerRoles)
                : "[]";

            AddAuditLog(
                channel.ServerId,
                "channel_permissions_updated",
                currentUsername,
                "channel",
                channel.Id,
                details: new
                {
                    channel.Name,
                    channel.Type,
                    channel.ViewAccessRestricted,
                    ViewAllowedRoleNames = viewRoles,
                    channel.MessageSendRestricted,
                    MessageSendAllowedRoleNames = sendRoles,
                    channel.VoiceAccessRestricted,
                    VoiceAllowedRoleNames = voiceAllowedRoles,
                    channel.StageSpeakerRestricted,
                    StageSpeakerRoleNames = stageSpeakerRoles
                });
            await _context.SaveChangesAsync();
            return Ok(await BuildChannelPermissionsResponse(channel));
        }

        [HttpGet]
        public async Task<IActionResult> GetChannelVoicePermissions(string channelId)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            var channel = await _context.Channels.FirstOrDefaultAsync(c => c.Id == channelId);
            if (channel == null)
                return NotFound(new { Message = "Channel not found." });

            if (!await CanViewChannel(channel, currentUsername))
                return Forbid();

            if (!IsVoiceLikeChannelType(channel.Type))
                return BadRequest(new { Message = "Permissions are only available for voice and stage channels." });

            await EnsureDefaultRoles(channel.ServerId);
            return Ok(await BuildChannelPermissionsResponse(channel));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateChannelVoicePermissions([FromBody] ChannelVoicePermissionsRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            var channel = await _context.Channels.FirstOrDefaultAsync(c => c.Id == request.ChannelId);
            if (channel == null)
                return NotFound(new { Message = "Channel not found." });

            if (!await CanManageChannels(channel.ServerId, currentUsername))
                return Forbid();

            if (!IsVoiceLikeChannelType(channel.Type))
                return BadRequest(new { Message = "Permissions are only available for voice and stage channels." });

            await EnsureDefaultRoles(channel.ServerId);
            var validRoleNames = (await _context.ServerRoles
                    .Where(role => role.ServerId == channel.ServerId)
                    .Select(role => role.Name)
                    .ToListAsync())
                .Select(NormalizeRoleName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var voiceAllowedRoles = NormalizeRequestedRoleNames(request.VoiceAllowedRoleNames, validRoleNames);
            var stageSpeakerRoles = NormalizeRequestedRoleNames(request.StageSpeakerRoleNames, validRoleNames);

            if (request.VoiceAccessRestricted && voiceAllowedRoles.Length == 0)
            {
                return BadRequest(new { Message = "At least one role must be allowed to connect." });
            }

            if (channel.Type == "stage" && request.StageSpeakerRestricted && stageSpeakerRoles.Length == 0)
            {
                return BadRequest(new { Message = "At least one role must be allowed to speak on stage." });
            }

            channel.VoiceAccessRestricted = request.VoiceAccessRestricted;
            channel.VoiceAllowedRolesJson = request.VoiceAccessRestricted
                ? SerializeRoleNames(voiceAllowedRoles)
                : "[]";
            channel.StageSpeakerRestricted = channel.Type == "stage" && request.StageSpeakerRestricted;
            channel.StageSpeakerRolesJson = channel.StageSpeakerRestricted
                ? SerializeRoleNames(stageSpeakerRoles)
                : "[]";

            AddAuditLog(
                channel.ServerId,
                "channel_voice_permissions_updated",
                currentUsername,
                "channel",
                channel.Id,
                details: new
                {
                    channel.Name,
                    channel.Type,
                    channel.VoiceAccessRestricted,
                    VoiceAllowedRoleNames = voiceAllowedRoles,
                    channel.StageSpeakerRestricted,
                    StageSpeakerRoleNames = stageSpeakerRoles
                });
            await _context.SaveChangesAsync();
            return Ok(await BuildChannelPermissionsResponse(channel));
        }

        [HttpPost]
        public async Task<IActionResult> DeleteChannel([FromBody] DeleteChannelRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            var channel = await _context.Channels.FirstOrDefaultAsync(c => c.Id == request.ChannelId);
            if (channel == null)
                return NotFound(new { Message = "Channel not found." });

            if (!await CanManageChannels(channel.ServerId, currentUsername))
                return Forbid();

            var messages = await _context.ServerMessages.Where(message => message.ChannelId == channel.Id).ToListAsync();
            var webhooks = await _context.ServerWebhooks.Where(webhook => webhook.ChannelId == channel.Id).ToListAsync();
            _context.ServerMessages.RemoveRange(messages);
            _context.ServerWebhooks.RemoveRange(webhooks);
            _context.Channels.Remove(channel);
            AddAuditLog(
                channel.ServerId,
                "channel_deleted",
                currentUsername,
                "channel",
                channel.Id,
                details: new { channel.Name, channel.Type, MessageCount = messages.Count, WebhookCount = webhooks.Count });
            await _context.SaveChangesAsync();
            InvalidatePublicServerDiscoveryCache();
            return Ok(new { Message = "Channel deleted." });
        }

        [HttpPost]
        public async Task<IActionResult> CreateCategory([FromBody] CategoryMutationRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageChannels(request.ServerId, currentUsername))
                return Forbid();

            var name = NormalizeName(request.Name);
            if (!await _context.CreateServers.AnyAsync(server => server.ServerID == request.ServerId))
                return NotFound(new { Message = "Server not found." });
            if (!IsValidCategoryName(name))
                return BadRequest(new { Message = "Category name must be 1-80 characters." });

            var category = new Category
            {
                Id = Guid.NewGuid().ToString(),
                ServerId = request.ServerId,
                Name = name,
                Position = await _context.Categories.CountAsync(c => c.ServerId == request.ServerId)
            };

            _context.Categories.Add(category);
            AddAuditLog(
                request.ServerId,
                "category_created",
                currentUsername,
                "category",
                category.Id,
                details: new { category.Name });
            await _context.SaveChangesAsync();
            return Ok(category);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateCategory([FromBody] CategoryMutationRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == request.CategoryId);
            if (category == null)
                return NotFound(new { Message = "Category not found." });

            if (!await CanManageChannels(category.ServerId, currentUsername))
                return Forbid();

            var name = NormalizeName(request.Name);
            if (!IsValidCategoryName(name))
                return BadRequest(new { Message = "Category name must be 1-80 characters." });

            var previousName = category.Name;
            category.Name = name;
            AddAuditLog(
                category.ServerId,
                "category_updated",
                currentUsername,
                "category",
                category.Id,
                details: new { PreviousName = previousName, CurrentName = category.Name });
            await _context.SaveChangesAsync();
            return Ok(category);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteCategory([FromBody] DeleteCategoryRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == request.CategoryId);
            if (category == null)
                return NotFound(new { Message = "Category not found." });

            if (!await CanManageChannels(category.ServerId, currentUsername))
                return Forbid();

            var channels = await _context.Channels.Where(channel => channel.CategoryId == category.Id).ToListAsync();
            foreach (var channel in channels)
            {
                channel.CategoryId = null;
            }

            _context.Categories.Remove(category);
            AddAuditLog(
                category.ServerId,
                "category_deleted",
                currentUsername,
                "category",
                category.Id,
                details: new { category.Name, DetachedChannelCount = channels.Count });
            await _context.SaveChangesAsync();
            return Ok(new { Message = "Category deleted." });
        }

        [HttpPost]
        public async Task<IActionResult> ReorderCategories([FromBody] ReorderCategoriesRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageChannels(request.ServerId, currentUsername))
                return Forbid();

            var categories = await _context.Categories
                .Where(category => category.ServerId == request.ServerId)
                .ToListAsync();
            var positions = (request.CategoryIds ?? Array.Empty<string>())
                .Select((id, index) => new { id, index })
                .ToDictionary(item => item.id, item => item.index);

            foreach (var category in categories)
            {
                if (positions.TryGetValue(category.Id, out var position))
                {
                    category.Position = position;
                }
            }

            AddAuditLog(
                request.ServerId,
                "categories_reordered",
                currentUsername,
                details: new { CategoryIds = request.CategoryIds ?? Array.Empty<string>() });
            await _context.SaveChangesAsync();
            return Ok(new { Message = "Categories reordered." });
        }

        [HttpPost]
        public async Task<IActionResult> ReorderChannels([FromBody] ReorderChannelsRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageChannels(request.ServerId, currentUsername))
                return Forbid();

            var channels = await _context.Channels
                .Where(channel => channel.ServerId == request.ServerId)
                .ToListAsync();
            var positions = (request.ChannelIds ?? Array.Empty<string>())
                .Select((id, index) => new { id, index })
                .ToDictionary(item => item.id, item => item.index);

            foreach (var channel in channels)
            {
                if (positions.TryGetValue(channel.Id, out var position))
                {
                    channel.Position = position;
                    channel.CategoryId = string.IsNullOrWhiteSpace(request.CategoryId)
                        ? channel.CategoryId
                        : request.CategoryId;
                }
            }

            AddAuditLog(
                request.ServerId,
                "channels_reordered",
                currentUsername,
                "category",
                request.CategoryId,
                details: new { request.CategoryId, ChannelIds = request.ChannelIds ?? Array.Empty<string>() });
            await _context.SaveChangesAsync();
            return Ok(new { Message = "Channels reordered." });
        }

        [HttpPost]
        public async Task<IActionResult> LeaveServer([FromBody] ServerActionRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == request.ServerId);
            if (server == null)
                return NotFound(new { Message = "Server not found." });

            if (server.ServerOwner == currentUsername)
                return BadRequest(new { Message = "Transfer ownership before leaving this server." });

            var memberships = await _context.ServerMembers
                .Where(member => member.ServerId == request.ServerId && member.Username == currentUsername)
                .ToListAsync();
            _context.ServerMembers.RemoveRange(memberships);
            await _context.SaveChangesAsync();
            InvalidatePublicServerDiscoveryCache();
            await _hubContext.Clients.Group(request.ServerId).SendAsync("UserLeft", currentUsername);
            return Ok(new { Message = "Left server." });
        }

        [HttpPost]
        public async Task<IActionResult> KickMember([FromBody] MemberModerationRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageMembers(request.ServerId, currentUsername))
                return Forbid();

            var targetUsername = request.TargetUsername?.Trim();
            if (string.IsNullOrWhiteSpace(targetUsername))
                return BadRequest(new { Message = "Target username is required." });

            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == request.ServerId);
            if (server?.ServerOwner == targetUsername)
                return BadRequest(new { Message = "The server owner cannot be kicked." });

            var memberships = await _context.ServerMembers
                .Where(member => member.ServerId == request.ServerId && member.Username == targetUsername)
                .ToListAsync();
            if (!memberships.Any())
                return NotFound(new { Message = "Member not found." });

            _context.ServerMembers.RemoveRange(memberships);
            AddAuditLog(
                request.ServerId,
                "member_kicked",
                currentUsername,
                "member",
                targetUsername,
                targetUsername,
                new { Reason = request.Reason?.Trim() });
            await _context.SaveChangesAsync();
            InvalidatePublicServerDiscoveryCache();
            await _hubContext.Clients.Group(request.ServerId).SendAsync("UserLeft", targetUsername);
            return Ok(new { Message = "Member kicked." });
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> BanMember([FromBody] MemberModerationRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanBanMembers(request.ServerId, currentUsername))
                return Forbid();

            var targetUsername = request.TargetUsername?.Trim();
            if (string.IsNullOrWhiteSpace(targetUsername))
                return BadRequest(new { Message = "Target username is required." });

            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == request.ServerId);
            if (server == null)
                return NotFound(new { Message = "Server not found." });
            if (server.ServerOwner == targetUsername)
                return BadRequest(new { Message = "The server owner cannot be banned." });

            var existingBan = await _context.ServerBans.FirstOrDefaultAsync(b =>
                b.ServerId == request.ServerId && b.Username == targetUsername);
            if (existingBan == null)
            {
                _context.ServerBans.Add(new ServerBan
                {
                    Id = Guid.NewGuid().ToString(),
                    ServerId = request.ServerId,
                    Username = targetUsername,
                    BannedBy = currentUsername,
                    Reason = request.Reason?.Trim(),
                    CreatedAt = DateTime.UtcNow
                });
            }

            var memberships = await _context.ServerMembers
                .Where(member => member.ServerId == request.ServerId && member.Username == targetUsername)
                .ToListAsync();
            _context.ServerMembers.RemoveRange(memberships);
            AddAuditLog(
                request.ServerId,
                "member_banned",
                currentUsername,
                "member",
                targetUsername,
                targetUsername,
                new { Reason = request.Reason?.Trim() });
            await _context.SaveChangesAsync();
            InvalidatePublicServerDiscoveryCache();
            await _hubContext.Clients.Group(request.ServerId).SendAsync("UserLeft", targetUsername);
            return Ok(new { Message = "Member banned." });
        }

        [HttpPost]
        public async Task<IActionResult> UnbanMember([FromBody] MemberModerationRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanBanMembers(request.ServerId, currentUsername))
                return Forbid();

            var targetUsername = request.TargetUsername?.Trim();
            if (string.IsNullOrWhiteSpace(targetUsername))
                return BadRequest(new { Message = "Target username is required." });

            var bans = await _context.ServerBans
                .Where(ban => ban.ServerId == request.ServerId && ban.Username == targetUsername)
                .ToListAsync();
            if (!bans.Any())
                return NotFound(new { Message = "Ban not found." });

            _context.ServerBans.RemoveRange(bans);
            AddAuditLog(
                request.ServerId,
                "member_unbanned",
                currentUsername,
                "member",
                targetUsername,
                targetUsername);
            await _context.SaveChangesAsync();
            return Ok(new { Message = "Member unbanned." });
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> MuteMember([FromBody] MemberModerationRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageMembers(request.ServerId, currentUsername))
                return Forbid();

            if (!TryGetModerationUntil(request.DurationMinutes, allowIndefinite: true, out var mutedUntil, out var durationMessage))
                return BadRequest(new { Message = durationMessage });

            var target = await GetModerationTarget(request, "muted");
            if (target.Error != null)
                return target.Error;

            foreach (var member in target.Memberships)
            {
                member.IsMuted = true;
                member.MutedUntil = mutedUntil;
            }

            AddAuditLog(
                request.ServerId,
                "member_muted",
                currentUsername,
                "member",
                target.TargetUsername,
                target.TargetUsername,
                new
                {
                    Reason = request.Reason?.Trim(),
                    request.DurationMinutes,
                    MutedUntil = mutedUntil
                });
            await _context.SaveChangesAsync();
            await _hubContext.Clients.Group(request.ServerId).SendAsync("MemberModerationUpdated", request.ServerId, target.TargetUsername);
            return Ok(new { Message = "Member muted.", MutedUntil = mutedUntil });
        }

        [HttpPost]
        public async Task<IActionResult> UnmuteMember([FromBody] MemberModerationRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageMembers(request.ServerId, currentUsername))
                return Forbid();

            var target = await GetModerationTarget(request, "unmuted");
            if (target.Error != null)
                return target.Error;

            foreach (var member in target.Memberships)
            {
                member.IsMuted = false;
                member.MutedUntil = null;
            }

            AddAuditLog(
                request.ServerId,
                "member_unmuted",
                currentUsername,
                "member",
                target.TargetUsername,
                target.TargetUsername,
                new { Reason = request.Reason?.Trim() });
            await _context.SaveChangesAsync();
            await _hubContext.Clients.Group(request.ServerId).SendAsync("MemberModerationUpdated", request.ServerId, target.TargetUsername);
            return Ok(new { Message = "Member unmuted." });
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> TimeoutMember([FromBody] MemberModerationRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageMembers(request.ServerId, currentUsername))
                return Forbid();

            if (!TryGetModerationUntil(request.DurationMinutes, allowIndefinite: false, out var timedOutUntil, out var durationMessage))
                return BadRequest(new { Message = durationMessage });

            var target = await GetModerationTarget(request, "timed out");
            if (target.Error != null)
                return target.Error;

            foreach (var member in target.Memberships)
            {
                member.TimedOutUntil = timedOutUntil;
            }

            AddAuditLog(
                request.ServerId,
                "member_timed_out",
                currentUsername,
                "member",
                target.TargetUsername,
                target.TargetUsername,
                new
                {
                    Reason = request.Reason?.Trim(),
                    request.DurationMinutes,
                    TimedOutUntil = timedOutUntil
                });
            await _context.SaveChangesAsync();
            await _hubContext.Clients.Group(request.ServerId).SendAsync("MemberModerationUpdated", request.ServerId, target.TargetUsername);
            return Ok(new { Message = "Member timed out.", TimedOutUntil = timedOutUntil });
        }

        [HttpPost]
        public async Task<IActionResult> ClearMemberTimeout([FromBody] MemberModerationRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageMembers(request.ServerId, currentUsername))
                return Forbid();

            var target = await GetModerationTarget(request, "restored");
            if (target.Error != null)
                return target.Error;

            foreach (var member in target.Memberships)
            {
                member.TimedOutUntil = null;
            }

            AddAuditLog(
                request.ServerId,
                "member_timeout_cleared",
                currentUsername,
                "member",
                target.TargetUsername,
                target.TargetUsername,
                new { Reason = request.Reason?.Trim() });
            await _context.SaveChangesAsync();
            await _hubContext.Clients.Group(request.ServerId).SendAsync("MemberModerationUpdated", request.ServerId, target.TargetUsername);
            return Ok(new { Message = "Member timeout cleared." });
        }

        [HttpGet]
        public async Task<IActionResult> GetBans(string serverId)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanBanMembers(serverId, currentUsername))
                return Forbid();

            var bans = await _context.ServerBans
                .Where(ban => ban.ServerId == serverId)
                .OrderByDescending(ban => ban.CreatedAt)
                .ToListAsync();
            return Ok(bans);
        }

        [HttpGet]
        public async Task<IActionResult> SearchMembers(string serverId, string query = "")
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await IsServerMember(serverId, currentUsername))
                return Forbid();

            query = query?.Trim() ?? string.Empty;
            var now = DateTime.UtcNow;
            var members = await _context.ServerMembers
                .Where(member => member.ServerId == serverId)
                .Where(member => query == string.Empty || member.Username.Contains(query))
                .OrderBy(member => member.Username)
                .Take(50)
                .ToListAsync();

            var memberResponses = members
                .Select(member => BuildMemberResponse(member, member.Username, member.Role, now))
                .ToList();
            var botResponses = await _context.BotAccounts
                .Where(bot => bot.ServerId == serverId)
                .Where(bot => query == string.Empty ||
                              bot.Username.Contains(query) ||
                              bot.DisplayName.Contains(query))
                .OrderBy(bot => bot.DisplayName)
                .Take(50)
                .ToListAsync();
            memberResponses.AddRange(botResponses.Select(BuildBotMemberResponse));
            var usernames = memberResponses
                .Where(member => !member.IsBot)
                .Select(member => member.Username)
                .ToList();
            var accounts = await _context.Accounts
                .Where(account => usernames.Contains(account.UserName) && !account.IsDisabled)
                .ToDictionaryAsync(account => account.UserName, StringComparer.OrdinalIgnoreCase);

            foreach (var member in memberResponses)
            {
                accounts.TryGetValue(member.Username, out var account);
                ApplyMemberAccountData(member, account);
            }

            return Ok(memberResponses);
        }

        [HttpGet]
        public async Task<IActionResult> GetRoles(string serverId)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await IsServerMember(serverId, currentUsername))
                return Forbid();

            await EnsureDefaultRoles(serverId);
            return Ok(await _context.ServerRoles
                .Where(role => role.ServerId == serverId)
                .OrderBy(role => role.Position)
                .ThenBy(role => role.Name)
                .ToListAsync());
        }

        [HttpPost]
        public async Task<IActionResult> UpsertRole([FromBody] RoleMutationRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageServer(request.ServerId, currentUsername))
                return Forbid();

            var roleName = NormalizeRoleName(request.Name);
            if (!IsValidRoleName(roleName))
                return BadRequest(new { Message = "Role name must be 1-40 characters." });
            if (roleName == "owner")
                return BadRequest(new { Message = "The owner role is managed automatically." });
            if (!string.IsNullOrWhiteSpace(request.Color) && !IsValidHexColor(request.Color))
                return BadRequest(new { Message = "Role color must be a valid hex color." });

            var role = !string.IsNullOrWhiteSpace(request.RoleId)
                ? await _context.ServerRoles.FirstOrDefaultAsync(r => r.Id == request.RoleId && r.ServerId == request.ServerId)
                : await _context.ServerRoles.FirstOrDefaultAsync(r => r.ServerId == request.ServerId && r.Name == roleName);

            var isNewRole = role == null;
            if (role == null)
            {
                role = new ServerRole
                {
                    Id = Guid.NewGuid().ToString(),
                    ServerId = request.ServerId,
                    Position = await _context.ServerRoles.CountAsync(r => r.ServerId == request.ServerId)
                };
                _context.ServerRoles.Add(role);
            }

            role.Name = roleName;
            role.Color = !string.IsNullOrWhiteSpace(request.Color)
                ? request.Color.Trim().ToLowerInvariant()
                : IsValidHexColor(role.Color)
                    ? role.Color
                    : GetDefaultRoleColor(roleName);
            role.CanManageServer = request.CanManageServer;
            role.CanManageChannels = request.CanManageChannels;
            role.CanManageMembers = request.CanManageMembers;
            role.CanBanMembers = request.CanBanMembers;
            role.CanCreateInvites = request.CanCreateInvites;
            role.CanSendMessages = request.CanSendMessages;
            role.CanJoinVoice = request.CanJoinVoice;

            AddAuditLog(
                request.ServerId,
                isNewRole ? "role_created" : "role_updated",
                currentUsername,
                "role",
                role.Id,
                details: new
                {
                    role.Name,
                    role.Color,
                    role.CanManageServer,
                    role.CanManageChannels,
                    role.CanManageMembers,
                    role.CanBanMembers,
                    role.CanCreateInvites,
                    role.CanSendMessages,
                    role.CanJoinVoice
                });
            await _context.SaveChangesAsync();
            return Ok(role);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteRole([FromBody] RoleActionRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageServer(request.ServerId, currentUsername))
                return Forbid();

            var role = await _context.ServerRoles.FirstOrDefaultAsync(r => r.Id == request.RoleId && r.ServerId == request.ServerId);
            if (role == null)
                return NotFound(new { Message = "Role not found." });
            if (role.Name is "owner" or "user")
                return BadRequest(new { Message = "Built-in owner/user roles cannot be deleted." });

            foreach (var member in _context.ServerMembers.Where(member =>
                         member.ServerId == request.ServerId && member.Role == role.Name))
            {
                member.Role = "user";
            }

            _context.ServerRoles.Remove(role);
            AddAuditLog(
                request.ServerId,
                "role_deleted",
                currentUsername,
                "role",
                role.Id,
                details: new { role.Name });
            await _context.SaveChangesAsync();
            return Ok(new { Message = "Role deleted." });
        }

        [HttpPost]
        public async Task<IActionResult> SetMemberRole([FromBody] MemberRoleRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageMembers(request.ServerId, currentUsername))
                return Forbid();

            var targetUsername = request.TargetUsername?.Trim();
            var roleName = NormalizeRoleName(request.Role);
            if (string.IsNullOrWhiteSpace(targetUsername) || !IsValidRoleName(roleName))
                return BadRequest(new { Message = "Target username and role are required." });
            if (roleName == "owner")
                return BadRequest(new { Message = "Use TransferOwnership to assign the owner role." });

            await EnsureDefaultRoles(request.ServerId);
            var role = await _context.ServerRoles.FirstOrDefaultAsync(role =>
                role.ServerId == request.ServerId && role.Name == roleName);
            if (role == null)
                return BadRequest(new { Message = "Role does not exist." });

            var member = await _context.ServerMembers.FirstOrDefaultAsync(m =>
                m.ServerId == request.ServerId && m.Username == targetUsername);
            if (member == null)
                return NotFound(new { Message = "Member not found." });

            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == request.ServerId);
            if (await RoleAssignmentViolatesTwoFactorRule(server, targetUsername, role))
            {
                return BadRequest(new { Message = "This server requires 2FA before assigning moderator or admin permissions." });
            }

            var previousRole = member.Role;
            member.Role = roleName;
            AddAuditLog(
                request.ServerId,
                "member_role_updated",
                currentUsername,
                "member",
                targetUsername,
                targetUsername,
                new { PreviousRole = previousRole, CurrentRole = roleName });
            await _context.SaveChangesAsync();
            return Ok(new { Message = "Member role updated." });
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> CreateInvite([FromBody] CreateInviteRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanCreateInvite(request.ServerId, currentUsername))
                return Forbid();

            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == request.ServerId);
            if (server == null)
                return NotFound(new { Message = "Server not found." });

            if (request.MaxUses is <= 0 or > 1000)
                return BadRequest(new { Message = "Max uses must be between 1 and 1000." });
            if (request.ExpiresInMinutes is <= 0 or > 60 * 24 * 30)
                return BadRequest(new { Message = "Expiry must be between 1 minute and 30 days." });

            var invite = new ServerInvite
            {
                Id = Guid.NewGuid().ToString(),
                ServerId = request.ServerId,
                Code = GenerateInviteCode(),
                CreatedBy = currentUsername,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = request.ExpiresInMinutes == null ? null : DateTime.UtcNow.AddMinutes(request.ExpiresInMinutes.Value),
                MaxUses = request.MaxUses
            };

            _context.ServerInvites.Add(invite);
            server.InviteLink = BuildInviteLink(Request, invite.Code);
            AddAuditLog(
                request.ServerId,
                "invite_created",
                currentUsername,
                "invite",
                invite.Id,
                details: new { invite.Code, invite.ExpiresAt, invite.MaxUses });
            await _context.SaveChangesAsync();

            return Ok(new
            {
                invite.Id,
                invite.Code,
                InviteLink = BuildInviteLink(Request, invite.Code),
                invite.ExpiresAt,
                invite.MaxUses,
                invite.Uses,
                invite.LastUsedAt,
                invite.AbuseDetectedAt,
                invite.AbuseReason
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetInvites(string serverId)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanCreateInvite(serverId, currentUsername))
                return Forbid();

            var invites = await _context.ServerInvites
                .Where(invite => invite.ServerId == serverId)
                .OrderByDescending(invite => invite.CreatedAt)
                .ToListAsync();

            return Ok(invites.Select(invite => new
            {
                invite.Id,
                invite.Code,
                InviteLink = BuildInviteLink(Request, invite.Code),
                invite.CreatedBy,
                invite.CreatedAt,
                invite.ExpiresAt,
                invite.MaxUses,
                invite.Uses,
                invite.LastUsedAt,
                invite.AbuseDetectedAt,
                invite.AbuseReason,
                invite.RevokedAt,
                IsActive = invite.RevokedAt == null &&
                           invite.AbuseDetectedAt == null &&
                           (invite.ExpiresAt == null || invite.ExpiresAt > DateTime.UtcNow) &&
                           (invite.MaxUses == null || invite.Uses < invite.MaxUses)
            }));
        }

        [HttpPost]
        public async Task<IActionResult> RevokeInvite([FromBody] InviteActionRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            var invite = await _context.ServerInvites.FirstOrDefaultAsync(i => i.Id == request.InviteId);
            if (invite == null)
                return NotFound(new { Message = "Invite not found." });

            if (!await CanCreateInvite(invite.ServerId, currentUsername))
                return Forbid();

            invite.RevokedAt ??= DateTime.UtcNow;
            AddAuditLog(
                invite.ServerId,
                "invite_revoked",
                currentUsername,
                "invite",
                invite.Id,
                details: new { invite.Code });
            await _context.SaveChangesAsync();
            return Ok(new { Message = "Invite revoked." });
        }

        [HttpPost]
        public async Task<IActionResult> TransferOwnership([FromBody] MemberModerationRequest request)
        {
            var currentUsername = GetCurrentUsername();
            if (currentUsername == null)
                return Unauthorized(new { Message = "Missing user identity." });

            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == request.ServerId);
            if (server == null)
                return NotFound(new { Message = "Server not found." });
            if (server.ServerOwner != currentUsername)
                return Forbid();

            var targetUsername = request.TargetUsername?.Trim();
            if (string.IsNullOrWhiteSpace(targetUsername))
                return BadRequest(new { Message = "Target username is required." });
            if (!await IsServerMember(request.ServerId, targetUsername))
                return BadRequest(new { Message = "Target user must be a server member." });

            var previousOwner = server.ServerOwner;
            server.ServerOwner = targetUsername;
            foreach (var member in _context.ServerMembers.Where(member => member.ServerId == request.ServerId))
            {
                member.Role = member.Username == targetUsername ? "owner" : "user";
            }

            AddAuditLog(
                request.ServerId,
                "ownership_transferred",
                currentUsername,
                "member",
                targetUsername,
                targetUsername,
                new { PreviousOwner = previousOwner, CurrentOwner = targetUsername });
            await _context.SaveChangesAsync();
            return Ok(new { Message = "Ownership transferred." });
        }

        private string? GetCurrentUsername()
        {
            return User.GetUsername();
        }

        private async Task<(IActionResult? Error, string TargetUsername, List<ServerMember> Memberships)> GetModerationTarget(
            MemberModerationRequest request,
            string ownerAction)
        {
            if (string.IsNullOrWhiteSpace(request.ServerId))
            {
                return (BadRequest(new { Message = "Server id is required." }), string.Empty, new List<ServerMember>());
            }

            var targetUsername = request.TargetUsername?.Trim();
            if (string.IsNullOrWhiteSpace(targetUsername))
            {
                return (BadRequest(new { Message = "Target username is required." }), string.Empty, new List<ServerMember>());
            }

            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == request.ServerId);
            if (server == null)
            {
                return (NotFound(new { Message = "Server not found." }), targetUsername, new List<ServerMember>());
            }

            if (string.Equals(server.ServerOwner, targetUsername, StringComparison.OrdinalIgnoreCase))
            {
                return (BadRequest(new { Message = $"The server owner cannot be {ownerAction}." }), targetUsername, new List<ServerMember>());
            }

            var memberships = await _context.ServerMembers
                .Where(member => member.ServerId == request.ServerId && member.Username == targetUsername)
                .ToListAsync();
            if (!memberships.Any())
            {
                return (NotFound(new { Message = "Member not found." }), targetUsername, memberships);
            }

            return (null, targetUsername, memberships);
        }

        private async Task<bool> IsServerMember(string serverId, string username)
        {
            return await _context.ServerMembers.AnyAsync(member =>
                member.ServerId == serverId && member.Username == username);
        }

        private async Task<IActionResult> JoinServerCore(
            string normalizedUsername,
            CreateServer server,
            ServerInvite? invite = null)
        {
            if (server == null || string.IsNullOrWhiteSpace(server.ServerID))
                return NotFound(new { Message = "Server not found" });

            if (await _context.ServerBans.AnyAsync(ban =>
                    ban.ServerId == server.ServerID && ban.Username == normalizedUsername))
            {
                return Forbid();
            }

            var role = server.ServerOwner == normalizedUsername ? "owner" : "user";
            var serverId = server.ServerID ?? throw new InvalidOperationException("ServerID is null");

            var existingMemberships = await _context.ServerMembers
                .Where(member => member.ServerId == serverId && member.Username == normalizedUsername)
                .ToListAsync();

            if (existingMemberships.Any())
            {
                var preservedMembership = existingMemberships
                    .OrderByDescending(member => member.Role == "owner")
                    .ThenBy(member => member.Id)
                    .First();

                var duplicateMemberships = existingMemberships
                    .Where(member => member.Id != preservedMembership.Id)
                    .ToList();

                var didMutateMemberships = false;

                if (preservedMembership.Role != role)
                {
                    preservedMembership.Role = role;
                    didMutateMemberships = true;
                }

                if (duplicateMemberships.Any())
                {
                    _context.ServerMembers.RemoveRange(duplicateMemberships);
                    didMutateMemberships = true;
                }

                if (didMutateMemberships)
                {
                    await _context.SaveChangesAsync();
                }

                return Ok(BuildServerResponse(
                    server,
                    role,
                    alreadyMember: true,
                    onboardingCompletedAt: preservedMembership.OnboardingCompletedAt));
            }

            if (server.ServerOwner != normalizedUsername)
            {
                var account = await _context.Accounts.FirstOrDefaultAsync(account =>
                    account.UserName == normalizedUsername && !account.IsDisabled);
                var joinVerification = ServerVerificationPolicy.EvaluateJoin(server, account);
                if (!joinVerification.Allowed)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new
                    {
                        Message = joinVerification.Message,
                        VerificationLevel = joinVerification.Level
                    });
                }
            }

            if (invite != null)
            {
                var inviteAbuseBlock = await RejectInviteAbuseIfDetected(invite, normalizedUsername);
                if (inviteAbuseBlock != null)
                {
                    return inviteAbuseBlock;
                }
            }

            var membership = new ServerMember
            {
                Id = Guid.NewGuid().ToString(),
                ServerId = serverId,
                Username = normalizedUsername,
                Role = role,
                JoinedAt = DateTime.UtcNow
            };

            _context.ServerMembers.Add(membership);
            if (invite != null)
            {
                invite.Uses += 1;
                invite.LastUsedAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();
            InvalidatePublicServerDiscoveryCache();

            await _hubContext.Clients.Group(serverId).SendAsync("NewMember", normalizedUsername);

            return Ok(BuildServerResponse(server, role, onboardingCompletedAt: membership.OnboardingCompletedAt));
        }

        private async Task<IActionResult?> RejectInviteAbuseIfDetected(ServerInvite invite, string joinedUsername)
        {
            if (_inviteAbuseDetectionService == null)
            {
                return null;
            }

            var result = await _inviteAbuseDetectionService.CheckAndTrackAsync(
                new InviteAbuseDetectionRequest(
                    invite.ServerId,
                    invite.Id,
                    invite.Code,
                    joinedUsername,
                    GetClientIpAddress()),
                HttpContext.RequestAborted);

            if (result.Allowed)
            {
                return null;
            }

            var now = DateTime.UtcNow;
            invite.AbuseDetectedAt = now;
            invite.AbuseReason = result.ReasonCode;
            if (result.ShouldRevokeInvite)
            {
                invite.RevokedAt ??= now;
            }

            AddAuditLog(
                invite.ServerId,
                "invite_abuse_detected",
                "automod",
                "invite",
                invite.Id,
                details: new
                {
                    invite.Code,
                    JoinedUsername = joinedUsername,
                    result.ReasonCode,
                    result.RecentInviteUses,
                    result.RecentIpUses,
                    Revoked = result.ShouldRevokeInvite
                });
            await _context.SaveChangesAsync();

            if (result.RetryAfterSeconds > 0)
            {
                Response.Headers.RetryAfter = result.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            }

            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                Message = result.Message,
                Reason = result.ReasonCode,
                RetryAfterSeconds = result.RetryAfterSeconds
            });
        }

        private string? GetClientIpAddress()
        {
            var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwardedFor))
            {
                return forwardedFor.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();
            }

            return HttpContext.Connection.RemoteIpAddress?.ToString();
        }

        private async Task<bool> CanManageServer(string serverId, string username)
        {
            return await HasPermission(serverId, username, role => role.CanManageServer, requireModeratorTwoFactor: true);
        }

        private async Task<bool> CanManageChannels(string serverId, string username)
        {
            return await HasPermission(serverId, username, role => role.CanManageChannels, requireModeratorTwoFactor: true);
        }

        private async Task<bool> CanManageMembers(string serverId, string username)
        {
            return await HasPermission(serverId, username, role => role.CanManageMembers, requireModeratorTwoFactor: true);
        }

        private async Task<bool> CanBanMembers(string serverId, string username)
        {
            return await HasPermission(serverId, username, role => role.CanBanMembers, requireModeratorTwoFactor: true);
        }

        private async Task<bool> CanCreateInvite(string serverId, string username)
        {
            return await HasPermission(serverId, username, role => role.CanCreateInvites, requireModeratorTwoFactor: true);
        }

        private async Task<bool> CanSendMessages(string serverId, string username)
        {
            return await HasPermission(serverId, username, role => role.CanSendMessages);
        }

        private async Task<bool> CanJoinVoice(string serverId, string username)
        {
            return await HasPermission(serverId, username, role => role.CanJoinVoice);
        }

        private async Task<bool> HasPermission(
            string serverId,
            string username,
            Func<ServerRole, bool> permission,
            bool requireModeratorTwoFactor = false)
        {
            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == serverId);
            if (server == null)
            {
                return false;
            }

            if (server.ServerOwner == username)
            {
                return true;
            }

            var member = await _context.ServerMembers.FirstOrDefaultAsync(m =>
                m.ServerId == serverId && m.Username == username);
            if (member == null)
            {
                return false;
            }

            await EnsureDefaultRoles(serverId);
            var roleName = NormalizeRoleName(member.Role);
            var role = await _context.ServerRoles.FirstOrDefaultAsync(r => r.ServerId == serverId && r.Name == roleName);
            role ??= roleName switch
            {
                "owner" => BuildDefaultRole(serverId, "owner", 0),
                "admin" => BuildDefaultRole(serverId, "admin", 1),
                "moderator" => BuildDefaultRole(serverId, "moderator", 2),
                "user" => BuildDefaultRole(serverId, "user", 3),
                _ => null
            };

            if (role == null || !permission(role))
            {
                return false;
            }

            if (requireModeratorTwoFactor && await RoleActionViolatesTwoFactorRule(server, username, role))
            {
                return false;
            }

            return true;
        }

        private async Task<bool> RoleActionViolatesTwoFactorRule(
            CreateServer server,
            string username,
            ServerRole role)
        {
            if (!server.RequireTwoFactorForModerators || !IsElevatedModeratorRole(role))
            {
                return false;
            }

            var account = await _context.Accounts.FirstOrDefaultAsync(account =>
                account.UserName == username && !account.IsDisabled);
            return account?.TwoFactorEnabled != true;
        }

        private async Task<bool> RoleAssignmentViolatesTwoFactorRule(
            CreateServer? server,
            string username,
            ServerRole role)
        {
            if (server == null ||
                server.ServerOwner == username ||
                !server.RequireTwoFactorForModerators ||
                !IsElevatedModeratorRole(role))
            {
                return false;
            }

            var account = await _context.Accounts.FirstOrDefaultAsync(account =>
                account.UserName == username && !account.IsDisabled);
            return account?.TwoFactorEnabled != true;
        }

        private static bool IsElevatedModeratorRole(ServerRole role)
        {
            var roleName = NormalizeRoleName(role.Name);
            return roleName is "owner" or "admin" or "moderator" ||
                   role.CanManageServer ||
                   role.CanManageChannels ||
                   role.CanManageMembers ||
                   role.CanBanMembers;
        }

        private async Task EnsureDefaultRoles(string serverId)
        {
            if (string.IsNullOrWhiteSpace(serverId))
            {
                return;
            }

            var existingRoleNames = await _context.ServerRoles
                .Where(role => role.ServerId == serverId)
                .Select(role => role.Name)
                .ToListAsync();
            var existing = existingRoleNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rolesToAdd = new[] { "owner", "admin", "moderator", "user" }
                .Where(role => !existing.Contains(role))
                .Select((role, index) => BuildDefaultRole(serverId, role, existing.Count + index))
                .ToList();

            if (rolesToAdd.Any())
            {
                _context.ServerRoles.AddRange(rolesToAdd);
            }

            var rolesToBackfill = await _context.ServerRoles
                .Where(role => role.ServerId == serverId)
                .ToListAsync();
            var backfilledRoleColors = false;
            foreach (var role in rolesToBackfill)
            {
                if (!IsValidHexColor(role.Color))
                {
                    role.Color = GetDefaultRoleColor(role.Name);
                    backfilledRoleColors = true;
                }
            }

            if (rolesToAdd.Any() || backfilledRoleColors)
            {
                await _context.SaveChangesAsync();
            }
        }

        private async Task<(CreateServer? Server, ServerMember? Member, ServerRole? Role)> GetChannelPermissionContext(
            string serverId,
            string username)
        {
            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == serverId);
            var member = await _context.ServerMembers.FirstOrDefaultAsync(m =>
                m.ServerId == serverId && m.Username == username);
            ServerRole? role = null;
            if (member != null)
            {
                await EnsureDefaultRoles(serverId);
                var roleName = NormalizeRoleName(member.Role);
                role = await _context.ServerRoles.FirstOrDefaultAsync(r =>
                    r.ServerId == serverId && r.Name == roleName);
            }

            return (server, member, role);
        }

        private async Task<bool> CanViewChannel(Channel channel, string username)
        {
            var (server, member, role) = await GetChannelPermissionContext(channel.ServerId, username);
            return ChannelPermissionPolicy.CanViewChannel(channel, server, member, role, username);
        }

        private async Task<object> BuildChannelPermissionsResponse(Channel channel)
        {
            var roles = await _context.ServerRoles
                .Where(role => role.ServerId == channel.ServerId)
                .OrderBy(role => role.Position)
                .ThenBy(role => role.Name)
                .ToListAsync();

            return new
            {
                channel.Id,
                channel.ServerId,
                channel.Name,
                channel.Type,
                channel.ViewAccessRestricted,
                ViewAllowedRoleNames = DeserializeRoleNames(channel.ViewAllowedRolesJson),
                channel.MessageSendRestricted,
                MessageSendAllowedRoleNames = DeserializeRoleNames(channel.MessageSendAllowedRolesJson),
                channel.VoiceAccessRestricted,
                VoiceAllowedRoleNames = DeserializeRoleNames(channel.VoiceAllowedRolesJson),
                channel.StageSpeakerRestricted,
                StageSpeakerRoleNames = DeserializeRoleNames(channel.StageSpeakerRolesJson),
                Roles = roles.Select(role => new
                {
                    role.Id,
                    Name = NormalizeRoleName(role.Name),
                    role.Color,
                    role.Position,
                    role.CanManageServer,
                    role.CanManageChannels,
                    role.CanSendMessages,
                    role.CanJoinVoice
                })
            };
        }

        private static string NormalizeName(string? value)
        {
            return value?.Trim() ?? string.Empty;
        }

        private static string? NormalizeChannelType(string? value)
        {
            var type = value?.Trim().ToLowerInvariant();
            return type is "text" or "voice" or "stage" ? type : null;
        }

        private static string? NormalizeDiscoveryCategory(string? value)
        {
            var category = value?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(category))
            {
                return null;
            }

            category = category.Replace(' ', '-');
            return category.Length <= 64 &&
                   category.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')
                ? category
                : null;
        }

        private static string? NormalizeDiscoveryTag(string? value)
        {
            var tag = value?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(tag))
            {
                return null;
            }

            tag = tag.Replace(' ', '-');
            return tag.Length <= MaxDiscoveryTagLength &&
                   tag.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
                ? tag
                : null;
        }

        private static string[] NormalizeDiscoveryTags(IEnumerable<string>? values)
        {
            return (values ?? Array.Empty<string>())
                .SelectMany(value => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(NormalizeDiscoveryTag)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag)
                .Take(MaxDiscoveryTags)
                .ToArray();
        }

        private static string SerializeDiscoveryTags(IEnumerable<string>? tags)
        {
            return JsonSerializer.Serialize(NormalizeDiscoveryTags(tags));
        }

        private static string[] DeserializeDiscoveryTags(string? tagsJson)
        {
            if (string.IsNullOrWhiteSpace(tagsJson))
            {
                return Array.Empty<string>();
            }

            try
            {
                return NormalizeDiscoveryTags(JsonSerializer.Deserialize<string[]>(tagsJson));
            }
            catch (JsonException)
            {
                return Array.Empty<string>();
            }
        }

        private static string[] DefaultWelcomeChecklist()
        {
            return new[]
            {
                "Read the welcome message",
                "Say hello in the general channel",
                "Join a voice channel when you are ready"
            };
        }

        private static string[] NormalizeWelcomeChecklist(IEnumerable<string>? values)
        {
            return (values ?? Array.Empty<string>())
                .SelectMany(value => (value ?? string.Empty).Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Length <= MaxWelcomeChecklistItemLength ? value : value[..MaxWelcomeChecklistItemLength])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxWelcomeChecklistItems)
                .ToArray();
        }

        private static string SerializeWelcomeChecklist(IEnumerable<string>? items)
        {
            var checklist = NormalizeWelcomeChecklist(items);
            return JsonSerializer.Serialize(checklist.Length > 0 ? checklist : DefaultWelcomeChecklist());
        }

        private static string[] DeserializeWelcomeChecklist(string? checklistJson)
        {
            if (string.IsNullOrWhiteSpace(checklistJson))
            {
                return DefaultWelcomeChecklist();
            }

            try
            {
                var checklist = NormalizeWelcomeChecklist(JsonSerializer.Deserialize<string[]>(checklistJson));
                return checklist.Length > 0 ? checklist : DefaultWelcomeChecklist();
            }
            catch (JsonException)
            {
                return DefaultWelcomeChecklist();
            }
        }

        private static string? NormalizeOptionalDescription(string? value, int maxLength)
        {
            var description = value?.Trim();
            if (string.IsNullOrWhiteSpace(description))
            {
                return null;
            }

            return description.Length <= maxLength
                ? description
                : description[..maxLength];
        }

        private static bool IsVoiceLikeChannelType(string? value)
        {
            var type = value?.Trim().ToLowerInvariant();
            return type is "voice" or "stage";
        }

        private static string[] DeserializeRoleNames(string? rolesJson)
        {
            if (string.IsNullOrWhiteSpace(rolesJson))
            {
                return Array.Empty<string>();
            }

            try
            {
                return (JsonSerializer.Deserialize<string[]>(rolesJson) ?? Array.Empty<string>())
                    .Select(NormalizeRoleName)
                    .Where(IsValidRoleName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(role => role)
                    .ToArray();
            }
            catch (JsonException)
            {
                return Array.Empty<string>();
            }
        }

        private static string[] NormalizeRequestedRoleNames(IEnumerable<string>? requestedRoles, ISet<string> validRoleNames)
        {
            return (requestedRoles ?? Array.Empty<string>())
                .Select(NormalizeRoleName)
                .Where(role => IsValidRoleName(role) && validRoleNames.Contains(role))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(role => role)
                .ToArray();
        }

        private static string SerializeRoleNames(IEnumerable<string> roleNames)
        {
            return JsonSerializer.Serialize(roleNames
                .Select(NormalizeRoleName)
                .Where(IsValidRoleName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(role => role)
                .ToArray());
        }

        private static string? NormalizeOptionalMediaUrl(string? value)
        {
            var normalized = value?.Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private static bool IsValidServerMediaUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return true;
            }

            if (url.Length > 2048)
            {
                return false;
            }

            if (url.StartsWith("/uploads/", StringComparison.Ordinal))
            {
                return true;
            }

            return Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl) &&
                   (parsedUrl.Scheme == Uri.UriSchemeHttp || parsedUrl.Scheme == Uri.UriSchemeHttps);
        }

        private static bool IsValidHexColor(string? color)
        {
            if (string.IsNullOrWhiteSpace(color))
            {
                return false;
            }

            var normalized = color.Trim();
            return normalized.Length == 7 &&
                   normalized[0] == '#' &&
                   normalized.Skip(1).All(Uri.IsHexDigit);
        }

        private static bool IsValidServerName(string name)
        {
            return name.Length is >= 1 and <= 100;
        }

        private static bool IsValidChannelName(string name)
        {
            return name.Length is >= 1 and <= 80;
        }

        private static bool IsValidCategoryName(string name)
        {
            return name.Length is >= 1 and <= 80;
        }

        private static string NormalizeRoleName(string? value)
        {
            return (value?.Trim().ToLowerInvariant() ?? string.Empty)
                .Replace(' ', '-');
        }

        private static bool IsValidRoleName(string name)
        {
            return name.Length is >= 1 and <= 40 &&
                   name.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');
        }

        private static string GenerateInviteCode()
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        }

        private static string ExtractInviteCode(string inviteLink)
        {
            var trimmed = inviteLink.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                return uri.Segments.LastOrDefault()?.Trim('/') ?? trimmed;
            }

            var slashIndex = trimmed.LastIndexOf('/');
            return slashIndex >= 0 ? trimmed[(slashIndex + 1)..] : trimmed;
        }

    }

    public class ChannelMutationRequest
    {
        public string? ChannelId { get; set; }
        public string ServerId { get; set; } = string.Empty;
        public string? CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "text";
    }

    public class DeleteChannelRequest
    {
        public string ChannelId { get; set; } = string.Empty;
    }

    public class ChannelVoicePermissionsRequest
    {
        public string ChannelId { get; set; } = string.Empty;
        public bool VoiceAccessRestricted { get; set; }
        public string[] VoiceAllowedRoleNames { get; set; } = Array.Empty<string>();
        public bool StageSpeakerRestricted { get; set; }
        public string[] StageSpeakerRoleNames { get; set; } = Array.Empty<string>();
    }

    public class ChannelPermissionsRequest
    {
        public string ChannelId { get; set; } = string.Empty;
        public bool ViewAccessRestricted { get; set; }
        public string[] ViewAllowedRoleNames { get; set; } = Array.Empty<string>();
        public bool MessageSendRestricted { get; set; }
        public string[] MessageSendAllowedRoleNames { get; set; } = Array.Empty<string>();
        public bool VoiceAccessRestricted { get; set; }
        public string[] VoiceAllowedRoleNames { get; set; } = Array.Empty<string>();
        public bool StageSpeakerRestricted { get; set; }
        public string[] StageSpeakerRoleNames { get; set; } = Array.Empty<string>();
    }

    public class CategoryMutationRequest
    {
        public string? CategoryId { get; set; }
        public string ServerId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class DeleteCategoryRequest
    {
        public string CategoryId { get; set; } = string.Empty;
    }

    public class ServerActionRequest
    {
        public string ServerId { get; set; } = string.Empty;
    }

    public class PublicServerListingRequest
    {
        public string ServerId { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
        public string? Description { get; set; }
        public string? DiscoveryCategory { get; set; }
        public string[]? DiscoveryTags { get; set; }
    }

    public class ServerAppearanceRequest
    {
        public string ServerId { get; set; } = string.Empty;
        public string? ServerIconUrl { get; set; }
        public string? ServerBannerUrl { get; set; }
    }

    public class ServerWelcomeRequest
    {
        public string ServerId { get; set; } = string.Empty;
        public bool WelcomeEnabled { get; set; } = true;
        public string? WelcomeMessage { get; set; }
        public string[]? WelcomeChecklist { get; set; }
    }

    public class ServerVerificationLevelRequest
    {
        public string ServerId { get; set; } = string.Empty;
        public string VerificationLevel { get; set; } = "none";
        public bool? RequireVerifiedEmail { get; set; }
        public int? MinimumAccountAgeMinutes { get; set; }
        public int? MinimumMembershipMinutes { get; set; }
        public bool? RequireTwoFactorForModerators { get; set; }
    }

    public class MemberModerationRequest
    {
        public string ServerId { get; set; } = string.Empty;
        public string TargetUsername { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public int? DurationMinutes { get; set; }
    }

    public class ReorderCategoriesRequest
    {
        public string ServerId { get; set; } = string.Empty;
        public string[] CategoryIds { get; set; } = Array.Empty<string>();
    }

    public class ReorderChannelsRequest
    {
        public string ServerId { get; set; } = string.Empty;
        public string? CategoryId { get; set; }
        public string[] ChannelIds { get; set; } = Array.Empty<string>();
    }

    public class RoleMutationRequest
    {
        public string? RoleId { get; set; }
        public string ServerId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Color { get; set; }
        public bool CanManageServer { get; set; }
        public bool CanManageChannels { get; set; }
        public bool CanManageMembers { get; set; }
        public bool CanBanMembers { get; set; }
        public bool CanCreateInvites { get; set; } = true;
        public bool CanSendMessages { get; set; } = true;
        public bool CanJoinVoice { get; set; } = true;
    }

    public class RoleActionRequest
    {
        public string ServerId { get; set; } = string.Empty;
        public string RoleId { get; set; } = string.Empty;
    }

    public class MemberRoleRequest
    {
        public string ServerId { get; set; } = string.Empty;
        public string TargetUsername { get; set; } = string.Empty;
        public string Role { get; set; } = "user";
    }

    public class CreateInviteRequest
    {
        public string ServerId { get; set; } = string.Empty;
        public int? ExpiresInMinutes { get; set; }
        public int? MaxUses { get; set; }
    }

    public class InviteActionRequest
    {
        public string InviteId { get; set; } = string.Empty;
    }

    public class AutoModRuleRequest
    {
        public string? RuleId { get; set; }
        public string ServerId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TriggerType { get; set; } = "keyword";
        public string TriggerValue { get; set; } = string.Empty;
        public string ActionType { get; set; } = "block_message";
        public bool IsEnabled { get; set; } = true;
    }

    public class AutoModRuleActionRequest
    {
        public string ServerId { get; set; } = string.Empty;
        public string RuleId { get; set; } = string.Empty;
    }
}
