using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using DiscordCloneServer.Data;
using DiscordCloneServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

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

        public ServerController(ApiContext context, IConfiguration config, IHubContext<ChatHub> hubContext)
        {
            _context = context;
            _config = config;
            _hubContext = hubContext;
        }

        private static object BuildServerResponse(CreateServer server, string role, bool alreadyMember = false)
        {
            return new
            {
                server.ServerID,
                server.ServerName,
                server.ServerOwner,
                server.InviteLink,
                server.Date,
                VerificationLevel = ServerVerificationPolicy.NormalizeLevel(server.VerificationLevel),
                RequireVerifiedEmail = server.RequireVerifiedEmail,
                MinimumAccountAgeMinutes = ServerVerificationPolicy.NormalizeRequiredMinutes(server.MinimumAccountAgeMinutes),
                MinimumMembershipMinutes = ServerVerificationPolicy.NormalizeRequiredMinutes(server.MinimumMembershipMinutes),
                RequireTwoFactorForModerators = server.RequireTwoFactorForModerators,
                Role = role,
                AlreadyMember = alreadyMember
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

            Console.WriteLine($"making new server: '{createServer.ServerName}' by {createServer.ServerOwner}");

            if (!IsValidServerName(createServer.ServerName))
                return BadRequest(new { Message = "Server name must be 1-100 characters." });

            createServer.ServerID = Guid.NewGuid().ToString();
            createServer.Date = DateTime.UtcNow;
            createServer.VerificationLevel = ServerVerificationPolicy.None;
            createServer.RequireVerifiedEmail = false;
            createServer.MinimumAccountAgeMinutes = 0;
            createServer.MinimumMembershipMinutes = 0;
            createServer.RequireTwoFactorForModerators = false;
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

         
            var textCategory = new Category { Id = Guid.NewGuid().ToString(), ServerId = createServer.ServerID, Name = "Text Channels", Position = 0 };
            var voiceCategory = new Category { Id = Guid.NewGuid().ToString(), ServerId = createServer.ServerID, Name = "Voice Channels", Position = 1 };

            _context.Categories.AddRange(textCategory, voiceCategory);

           
            var generalText = new Channel { Id = Guid.NewGuid().ToString(), ServerId = createServer.ServerID, CategoryId = textCategory.Id, Name = "general", Type = "text", Position = 0 };
            var generalVoice = new Channel { Id = Guid.NewGuid().ToString(), ServerId = createServer.ServerID, CategoryId = voiceCategory.Id, Name = "General", Type = "voice", Position = 0 };

            _context.Channels.AddRange(generalText, generalVoice);
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

            var serverResponse = servers
                .Select(server =>
                {
                    var serverId = server.ServerID ?? string.Empty;
                    var role = membershipsByServerId.TryGetValue(serverId, out var membershipRole)
                        ? membershipRole
                        : server.ServerOwner == normalizedUsername
                            ? "owner"
                            : "user";

                    return BuildServerResponse(server, role);
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
                    invite.ExpiresAt <= DateTime.UtcNow ||
                    (invite.MaxUses != null && invite.Uses >= invite.MaxUses))
                {
                    return BadRequest(new { Message = "Invite link is expired or has reached its usage limit." });
                }

                server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == invite.ServerId);
            }

            server ??= await _context.CreateServers
                .FirstOrDefaultAsync(s => s.InviteLink == normalizedInviteLink);

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

                return Ok(BuildServerResponse(server, role, alreadyMember: true));
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
            }
            await _context.SaveChangesAsync();


            await _hubContext.Clients.Group(serverId).SendAsync("NewMember", normalizedUsername);

            return Ok(BuildServerResponse(server, role));
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

            return Ok(new
            {
                Server = BuildServerResponse(
                    server,
                    server.ServerOwner == currentUsername
                        ? "owner"
                        : await _context.ServerMembers
                            .Where(member => member.ServerId == serverId && member.Username == currentUsername)
                            .Select(member => member.Role)
                            .FirstOrDefaultAsync() ?? "user"),
                Categories = categories,
                Channels = channels
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

            var dedupedMembers = members
                .GroupBy(member => member.Username)
                .Select(group =>
                {
                    var ownerEntry = group.FirstOrDefault(member => member.Role == "owner");
                    var chosenEntry = ownerEntry ?? group.OrderBy(member => member.Id).First();

                    return new
                    {
                        chosenEntry.Id,
                        Username = group.Key,
                        Role = ownerEntry != null ? "owner" : chosenEntry.Role
                    };
                })
                .OrderBy(member => member.Role == "owner" ? 0 : 1)
                .ThenBy(member => member.Username)
                .ToList();

            return Ok(dedupedMembers);
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
            await _context.SaveChangesAsync();

            return Ok(BuildServerResponse(server, server.ServerOwner == currentUsername ? "owner" : "user"));
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
                return BadRequest(new { Message = "Channel type must be text or voice." });
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
            await _context.SaveChangesAsync();
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
                return BadRequest(new { Message = "Channel type must be text or voice." });
            if (!string.IsNullOrWhiteSpace(request.CategoryId) &&
                !await _context.Categories.AnyAsync(category => category.Id == request.CategoryId && category.ServerId == channel.ServerId))
                return BadRequest(new { Message = "Category does not belong to this server." });

            channel.Name = name;
            channel.Type = type;
            channel.CategoryId = string.IsNullOrWhiteSpace(request.CategoryId) ? null : request.CategoryId;

            await _context.SaveChangesAsync();
            return Ok(channel);
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
            _context.ServerMessages.RemoveRange(messages);
            _context.Channels.Remove(channel);
            await _context.SaveChangesAsync();
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

            category.Name = name;
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
            await _context.SaveChangesAsync();
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
            await _context.SaveChangesAsync();
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
            await _context.SaveChangesAsync();
            return Ok(new { Message = "Member unbanned." });
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
            var members = await _context.ServerMembers
                .Where(member => member.ServerId == serverId)
                .Where(member => query == string.Empty || member.Username.Contains(query))
                .OrderBy(member => member.Username)
                .Take(50)
                .Select(member => new
                {
                    member.Id,
                    member.Username,
                    member.Role
                })
                .ToListAsync();

            return Ok(members);
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

            var role = !string.IsNullOrWhiteSpace(request.RoleId)
                ? await _context.ServerRoles.FirstOrDefaultAsync(r => r.Id == request.RoleId && r.ServerId == request.ServerId)
                : await _context.ServerRoles.FirstOrDefaultAsync(r => r.ServerId == request.ServerId && r.Name == roleName);

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
            role.CanManageServer = request.CanManageServer;
            role.CanManageChannels = request.CanManageChannels;
            role.CanManageMembers = request.CanManageMembers;
            role.CanBanMembers = request.CanBanMembers;
            role.CanCreateInvites = request.CanCreateInvites;
            role.CanSendMessages = request.CanSendMessages;
            role.CanJoinVoice = request.CanJoinVoice;

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

            member.Role = roleName;
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
            await _context.SaveChangesAsync();

            return Ok(new
            {
                invite.Id,
                invite.Code,
                InviteLink = BuildInviteLink(Request, invite.Code),
                invite.ExpiresAt,
                invite.MaxUses,
                invite.Uses
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
                invite.RevokedAt,
                IsActive = invite.RevokedAt == null &&
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

            server.ServerOwner = targetUsername;
            foreach (var member in _context.ServerMembers.Where(member => member.ServerId == request.ServerId))
            {
                member.Role = member.Username == targetUsername ? "owner" : "user";
            }

            await _context.SaveChangesAsync();
            return Ok(new { Message = "Ownership transferred." });
        }

        private string? GetCurrentUsername()
        {
            return User.GetUsername();
        }

        private async Task<bool> IsServerMember(string serverId, string username)
        {
            return await _context.ServerMembers.AnyAsync(member =>
                member.ServerId == serverId && member.Username == username);
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
                await _context.SaveChangesAsync();
            }
        }

        private static string NormalizeName(string? value)
        {
            return value?.Trim() ?? string.Empty;
        }

        private static string? NormalizeChannelType(string? value)
        {
            var type = value?.Trim().ToLowerInvariant();
            return type is "text" or "voice" ? type : null;
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
}
