using System.Security.Cryptography;
using System.Text;
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
    public class ServerIntegrationsController : ControllerBase
    {
        private const int MaxNameLength = 80;
        private const int MaxDescriptionLength = 240;
        private const int MaxMessageLength = 4000;
        private const int MaxCommandNameLength = 32;
        private const int MaxCommandDescriptionLength = 120;
        private const int MaxCommandUsageLength = 120;
        private const int MaxInteractionArgumentsLength = 2000;
        private readonly ApiContext _context;

        public ServerIntegrationsController(ApiContext context)
        {
            _context = context;
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetBotAccounts(string serverId)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageServer(serverId, currentUsername))
                return Forbid();

            var bots = await _context.BotAccounts
                .Where(bot => bot.ServerId == serverId)
                .OrderBy(bot => bot.DisplayName)
                .ToListAsync();

            return Ok(bots.Select(bot => BuildBotResponse(bot)));
        }

        [Authorize]
        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> CreateBotAccount([FromBody] BotAccountMutationRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageServer(request.ServerId, currentUsername))
                return Forbid();

            var server = await _context.CreateServers.FirstOrDefaultAsync(item => item.ServerID == request.ServerId);
            if (server == null)
                return NotFound(new { Message = "Server not found." });

            var displayName = NormalizeName(request.Name);
            if (displayName == null)
                return BadRequest(new { Message = "Bot name must be 1-80 characters." });

            if (!await IsIntegrationNameAvailable(request.ServerId, displayName))
                return Conflict(new { Message = "A member or bot already uses that name in this server." });

            var avatarUrl = NormalizeOptional(request.AvatarUrl);
            if (!IsValidMediaUrl(avatarUrl))
                return BadRequest(new { Message = "Avatar must be blank, an http URL, or an uploaded file URL." });

            var roleName = await NormalizeAssignableRole(request.ServerId, request.Role);
            if (roleName == null)
                return BadRequest(new { Message = "Bot role must be a server role other than owner." });

            var token = GenerateToken();
            var now = DateTime.UtcNow;
            var bot = new BotAccount
            {
                Id = Guid.NewGuid().ToString(),
                ServerId = request.ServerId,
                Username = displayName,
                DisplayName = displayName,
                AvatarUrl = avatarUrl,
                Description = NormalizeDescription(request.Description),
                Role = roleName,
                TokenHash = HashToken(token),
                CreatedBy = currentUsername,
                CreatedAt = now,
                TokenLastRotatedAt = now,
                IsEnabled = request.IsEnabled ?? true
            };

            _context.BotAccounts.Add(bot);
            await _context.SaveChangesAsync();
            return Ok(BuildBotResponse(bot, token));
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> UpdateBotAccount([FromBody] BotAccountMutationRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            var bot = await _context.BotAccounts.FirstOrDefaultAsync(item => item.Id == request.BotId);
            if (bot == null)
                return NotFound(new { Message = "Bot account not found." });

            if (!await CanManageServer(bot.ServerId, currentUsername))
                return Forbid();

            var displayName = NormalizeName(request.Name);
            if (displayName == null)
                return BadRequest(new { Message = "Bot name must be 1-80 characters." });

            if (!displayName.Equals(bot.DisplayName, StringComparison.OrdinalIgnoreCase) &&
                !await IsIntegrationNameAvailable(bot.ServerId, displayName))
            {
                return Conflict(new { Message = "A member or bot already uses that name in this server." });
            }

            var avatarUrl = NormalizeOptional(request.AvatarUrl);
            if (!IsValidMediaUrl(avatarUrl))
                return BadRequest(new { Message = "Avatar must be blank, an http URL, or an uploaded file URL." });

            var roleName = await NormalizeAssignableRole(bot.ServerId, request.Role);
            if (roleName == null)
                return BadRequest(new { Message = "Bot role must be a server role other than owner." });

            bot.Username = displayName;
            bot.DisplayName = displayName;
            bot.AvatarUrl = avatarUrl;
            bot.Description = NormalizeDescription(request.Description);
            bot.Role = roleName;
            bot.IsEnabled = request.IsEnabled ?? bot.IsEnabled;
            bot.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(BuildBotResponse(bot));
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> DeleteBotAccount([FromBody] BotAccountActionRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            var bot = await _context.BotAccounts.FirstOrDefaultAsync(item => item.Id == request.BotId);
            if (bot == null)
                return NotFound(new { Message = "Bot account not found." });

            if (!await CanManageServer(bot.ServerId, currentUsername))
                return Forbid();

            _context.BotAccounts.Remove(bot);
            await _context.SaveChangesAsync();
            return Ok(new { Message = "Bot account deleted." });
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> RotateBotToken([FromBody] BotAccountActionRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            var bot = await _context.BotAccounts.FirstOrDefaultAsync(item => item.Id == request.BotId);
            if (bot == null)
                return NotFound(new { Message = "Bot account not found." });

            if (!await CanManageServer(bot.ServerId, currentUsername))
                return Forbid();

            var token = GenerateToken();
            bot.TokenHash = HashToken(token);
            bot.TokenLastRotatedAt = DateTime.UtcNow;
            bot.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(BuildBotResponse(bot, token));
        }

        [AllowAnonymous]
        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> SendBotMessage([FromBody] BotMessageRequest request)
        {
            var token = GetProvidedToken(request.Token, "X-Bot-Token");
            if (string.IsNullOrWhiteSpace(token))
                return Unauthorized(new { Message = "Bot token is required." });

            var tokenHash = HashToken(token);
            var bot = await _context.BotAccounts.FirstOrDefaultAsync(item => item.TokenHash == tokenHash);
            if (bot == null || !bot.IsEnabled)
                return Unauthorized(new { Message = "Bot token is invalid." });

            var channel = await _context.Channels.FirstOrDefaultAsync(item =>
                item.Id == request.ChannelId && item.ServerId == bot.ServerId);
            if (channel == null)
                return NotFound(new { Message = "Channel not found." });
            if (channel.Type != "text")
                return BadRequest(new { Message = "Bots can only send messages to text channels." });
            if (!await CanBotSendToChannel(bot, channel))
                return Forbid();

            var result = await CreateIntegrationMessage(
                channel,
                bot.Username,
                bot.DisplayName,
                bot.AvatarUrl,
                request.UserText,
                request.AttachmentUrl,
                request.AttachmentContentType,
                isBot: true,
                botAccountId: bot.Id,
                isWebhook: false,
                webhookId: null,
                request.MessageId);

            return result;
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetSlashCommands(string serverId)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await IsServerMember(serverId, currentUsername))
                return Forbid();

            var canManage = await CanManageServer(serverId, currentUsername);
            var commands = await _context.SlashCommands
                .Where(command => command.ServerId == serverId)
                .OrderBy(command => command.Name)
                .ToListAsync();
            var botIds = commands.Select(command => command.BotAccountId).Distinct().ToList();
            var bots = await _context.BotAccounts
                .Where(bot => botIds.Contains(bot.Id))
                .ToDictionaryAsync(bot => bot.Id);

            return Ok(commands
                .Where(command =>
                    canManage ||
                    (bots.TryGetValue(command.BotAccountId, out var bot) && bot.IsEnabled && command.IsEnabled))
                .Select(command =>
                {
                    bots.TryGetValue(command.BotAccountId, out var bot);
                    return BuildSlashCommandResponse(command, bot, canManage);
                }));
        }

        [Authorize]
        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> CreateSlashCommand([FromBody] SlashCommandMutationRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageServer(request.ServerId, currentUsername))
                return Forbid();

            var bot = await _context.BotAccounts.FirstOrDefaultAsync(item =>
                item.Id == request.BotAccountId && item.ServerId == request.ServerId);
            if (bot == null)
                return NotFound(new { Message = "Bot account not found." });

            var name = NormalizeCommandName(request.Name);
            if (name == null)
                return BadRequest(new { Message = "Command name must be 1-32 lowercase letters, numbers, underscores, or dashes." });

            var description = NormalizeCommandDescription(request.Description);
            if (description == null)
                return BadRequest(new { Message = "Command description must be 1-120 characters." });

            var usage = NormalizeCommandUsage(request.Usage, name);
            if (usage == null)
                return BadRequest(new { Message = "Command usage must be 1-120 characters." });

            if (await _context.SlashCommands.AnyAsync(command =>
                    command.ServerId == request.ServerId && command.Name == name))
            {
                return Conflict(new { Message = $"A /{name} command already exists in this server." });
            }

            var command = new SlashCommand
            {
                Id = Guid.NewGuid().ToString(),
                ServerId = request.ServerId,
                BotAccountId = bot.Id,
                Name = name,
                Description = description,
                Usage = usage,
                CreatedBy = currentUsername,
                CreatedAt = DateTime.UtcNow,
                IsEnabled = request.IsEnabled ?? true
            };

            _context.SlashCommands.Add(command);
            await _context.SaveChangesAsync();
            return Ok(BuildSlashCommandResponse(command, bot, canManage: true));
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> UpdateSlashCommand([FromBody] SlashCommandMutationRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            var command = await _context.SlashCommands.FirstOrDefaultAsync(item => item.Id == request.CommandId);
            if (command == null)
                return NotFound(new { Message = "Slash command not found." });

            if (!await CanManageServer(command.ServerId, currentUsername))
                return Forbid();

            var bot = await _context.BotAccounts.FirstOrDefaultAsync(item =>
                item.Id == request.BotAccountId && item.ServerId == command.ServerId);
            if (bot == null)
                return NotFound(new { Message = "Bot account not found." });

            var name = NormalizeCommandName(request.Name);
            if (name == null)
                return BadRequest(new { Message = "Command name must be 1-32 lowercase letters, numbers, underscores, or dashes." });

            var description = NormalizeCommandDescription(request.Description);
            if (description == null)
                return BadRequest(new { Message = "Command description must be 1-120 characters." });

            var usage = NormalizeCommandUsage(request.Usage, name);
            if (usage == null)
                return BadRequest(new { Message = "Command usage must be 1-120 characters." });

            if (await _context.SlashCommands.AnyAsync(item =>
                    item.ServerId == command.ServerId &&
                    item.Name == name &&
                    item.Id != command.Id))
            {
                return Conflict(new { Message = $"A /{name} command already exists in this server." });
            }

            command.BotAccountId = bot.Id;
            command.Name = name;
            command.Description = description;
            command.Usage = usage;
            command.IsEnabled = request.IsEnabled ?? command.IsEnabled;
            command.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(BuildSlashCommandResponse(command, bot, canManage: true));
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> DeleteSlashCommand([FromBody] SlashCommandActionRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            var command = await _context.SlashCommands.FirstOrDefaultAsync(item => item.Id == request.CommandId);
            if (command == null)
                return NotFound(new { Message = "Slash command not found." });

            if (!await CanManageServer(command.ServerId, currentUsername))
                return Forbid();

            _context.SlashCommands.Remove(command);
            await _context.SaveChangesAsync();
            return Ok(new { Message = "Slash command deleted." });
        }

        [AllowAnonymous]
        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> UpsertBotSlashCommand([FromBody] BotSlashCommandRegistrationRequest request)
        {
            var bot = await GetBotFromToken(request.Token);
            if (bot == null)
                return Unauthorized(new { Message = "Bot token is invalid." });

            var name = NormalizeCommandName(request.Name);
            if (name == null)
                return BadRequest(new { Message = "Command name must be 1-32 lowercase letters, numbers, underscores, or dashes." });

            var description = NormalizeCommandDescription(request.Description);
            if (description == null)
                return BadRequest(new { Message = "Command description must be 1-120 characters." });

            var usage = NormalizeCommandUsage(request.Usage, name);
            if (usage == null)
                return BadRequest(new { Message = "Command usage must be 1-120 characters." });

            SlashCommand? command = null;
            if (!string.IsNullOrWhiteSpace(request.CommandId))
            {
                command = await _context.SlashCommands.FirstOrDefaultAsync(item =>
                    item.Id == request.CommandId && item.BotAccountId == bot.Id);
                if (command == null)
                    return NotFound(new { Message = "Slash command not found." });
            }
            else
            {
                command = await _context.SlashCommands.FirstOrDefaultAsync(item =>
                    item.ServerId == bot.ServerId && item.Name == name);
                if (command != null && command.BotAccountId != bot.Id)
                    return Conflict(new { Message = $"A /{name} command already exists in this server." });
            }

            if (command == null)
            {
                command = new SlashCommand
                {
                    Id = Guid.NewGuid().ToString(),
                    ServerId = bot.ServerId,
                    BotAccountId = bot.Id,
                    CreatedBy = bot.Username,
                    CreatedAt = DateTime.UtcNow
                };
                _context.SlashCommands.Add(command);
            }

            command.Name = name;
            command.Description = description;
            command.Usage = usage;
            command.IsEnabled = request.IsEnabled ?? true;
            command.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(BuildSlashCommandResponse(command, bot, canManage: false));
        }

        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> GetBotSlashCommands([FromBody] BotSlashCommandListRequest request)
        {
            var bot = await GetBotFromToken(request.Token);
            if (bot == null)
                return Unauthorized(new { Message = "Bot token is invalid." });

            var commands = await _context.SlashCommands
                .Where(command => command.BotAccountId == bot.Id)
                .OrderBy(command => command.Name)
                .ToListAsync();

            return Ok(commands.Select(command => BuildSlashCommandResponse(command, bot, canManage: false)));
        }

        [Authorize]
        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> ExecuteSlashCommand([FromBody] SlashCommandExecuteRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            var serverId = request.ServerId?.Trim() ?? string.Empty;
            var commandName = NormalizeCommandName(request.Name);
            var command = !string.IsNullOrWhiteSpace(request.CommandId)
                ? await _context.SlashCommands.FirstOrDefaultAsync(item =>
                    item.Id == request.CommandId && item.ServerId == serverId)
                : await _context.SlashCommands.FirstOrDefaultAsync(item =>
                    item.ServerId == serverId && item.Name == commandName);
            if (command == null || !command.IsEnabled)
                return NotFound(new { Message = "Slash command not found." });

            var bot = await _context.BotAccounts.FirstOrDefaultAsync(item =>
                item.Id == command.BotAccountId && item.ServerId == command.ServerId);
            if (bot == null || !bot.IsEnabled)
                return NotFound(new { Message = "Command bot is unavailable." });

            var channel = await _context.Channels.FirstOrDefaultAsync(item =>
                item.Id == request.ChannelId && item.ServerId == command.ServerId);
            if (channel == null)
                return NotFound(new { Message = "Channel not found." });
            if (channel.Type != "text")
                return BadRequest(new { Message = "Slash commands can only run in text channels." });

            var member = await _context.ServerMembers.FirstOrDefaultAsync(item =>
                item.ServerId == command.ServerId && item.Username == currentUsername);
            var server = await _context.CreateServers.FirstOrDefaultAsync(item => item.ServerID == command.ServerId);
            if (server?.ServerOwner != currentUsername && member == null)
                return Forbid();

            var communicationRestriction = GetCommunicationRestriction(member);
            if (communicationRestriction != null)
                return StatusCode(StatusCodes.Status403Forbidden, new { Message = communicationRestriction });

            if (!await CanUserSendToChannel(channel, currentUsername))
                return Forbid();

            var arguments = NormalizeInteractionArguments(request.Arguments);
            if (arguments == null)
                return BadRequest(new { Message = "Command arguments must be 2000 characters or fewer." });

            var interaction = new SlashCommandInteraction
            {
                Id = Guid.NewGuid().ToString(),
                SlashCommandId = command.Id,
                ServerId = command.ServerId,
                ChannelId = channel.Id,
                BotAccountId = bot.Id,
                CommandName = command.Name,
                InvokedBy = currentUsername,
                Arguments = arguments,
                CreatedAt = DateTime.UtcNow,
                Status = "pending"
            };

            _context.SlashCommandInteractions.Add(interaction);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                Interaction = BuildSlashInteractionResponse(interaction),
                Command = BuildSlashCommandResponse(command, bot, canManage: false),
                Message = $"Command /{command.Name} sent to {bot.DisplayName}."
            });
        }

        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> GetPendingSlashCommandInteractions([FromBody] BotPendingSlashCommandRequest request)
        {
            var bot = await GetBotFromToken(request.Token);
            if (bot == null)
                return Unauthorized(new { Message = "Bot token is invalid." });

            var take = Math.Clamp(request.Take <= 0 ? 25 : request.Take, 1, 100);
            var interactions = await _context.SlashCommandInteractions
                .Where(interaction =>
                    interaction.BotAccountId == bot.Id &&
                    (interaction.Status == "pending" || interaction.Status == "acknowledged"))
                .OrderBy(interaction => interaction.CreatedAt)
                .Take(take)
                .ToListAsync();

            return Ok(interactions.Select(BuildSlashInteractionResponse));
        }

        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> AcknowledgeSlashCommand([FromBody] SlashCommandInteractionActionRequest request)
        {
            var bot = await GetBotFromToken(request.Token);
            if (bot == null)
                return Unauthorized(new { Message = "Bot token is invalid." });

            var interaction = await _context.SlashCommandInteractions.FirstOrDefaultAsync(item =>
                item.Id == request.InteractionId && item.BotAccountId == bot.Id);
            if (interaction == null)
                return NotFound(new { Message = "Slash command interaction not found." });
            if (interaction.Status == "responded")
                return Conflict(new { Message = "Slash command interaction already has a response." });

            interaction.Status = "acknowledged";
            interaction.AcknowledgedAt ??= DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(BuildSlashInteractionResponse(interaction));
        }

        [AllowAnonymous]
        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> RespondToSlashCommand([FromBody] SlashCommandResponseRequest request)
        {
            var bot = await GetBotFromToken(request.Token);
            if (bot == null)
                return Unauthorized(new { Message = "Bot token is invalid." });

            var interaction = await _context.SlashCommandInteractions.FirstOrDefaultAsync(item =>
                item.Id == request.InteractionId && item.BotAccountId == bot.Id);
            if (interaction == null)
                return NotFound(new { Message = "Slash command interaction not found." });
            if (interaction.Status == "responded")
                return Conflict(new { Message = "Slash command interaction already has a response." });

            var channel = await _context.Channels.FirstOrDefaultAsync(item =>
                item.Id == interaction.ChannelId && item.ServerId == bot.ServerId);
            if (channel == null)
                return NotFound(new { Message = "Channel not found." });
            if (!await CanBotSendToChannel(bot, channel))
                return Forbid();

            var messageResult = await CreateIntegrationMessage(
                channel,
                bot.Username,
                bot.DisplayName,
                bot.AvatarUrl,
                request.Content,
                request.AttachmentUrl,
                request.AttachmentContentType,
                isBot: true,
                botAccountId: bot.Id,
                isWebhook: false,
                webhookId: null,
                request.MessageId);
            if (messageResult is not OkObjectResult ok)
            {
                return messageResult;
            }

            interaction.Status = "responded";
            interaction.AcknowledgedAt ??= DateTime.UtcNow;
            interaction.RespondedAt = DateTime.UtcNow;
            interaction.ResponseMessageId = ExtractMessageId(ok.Value);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                Interaction = BuildSlashInteractionResponse(interaction),
                Message = ok.Value
            });
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetWebhooks(string serverId)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageServer(serverId, currentUsername))
                return Forbid();

            var webhooks = await _context.ServerWebhooks
                .Where(webhook => webhook.ServerId == serverId)
                .OrderBy(webhook => webhook.Name)
                .ToListAsync();

            var channelIds = webhooks.Select(webhook => webhook.ChannelId).Distinct().ToList();
            var channels = await _context.Channels
                .Where(channel => channelIds.Contains(channel.Id))
                .ToDictionaryAsync(channel => channel.Id);

            return Ok(webhooks.Select(webhook => BuildWebhookResponse(
                webhook,
                channels.GetValueOrDefault(webhook.ChannelId))));
        }

        [Authorize]
        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> CreateWebhook([FromBody] WebhookMutationRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            if (!await CanManageServer(request.ServerId, currentUsername))
                return Forbid();

            var channel = await _context.Channels.FirstOrDefaultAsync(item =>
                item.Id == request.ChannelId && item.ServerId == request.ServerId);
            if (channel == null)
                return NotFound(new { Message = "Channel not found." });
            if (channel.Type != "text")
                return BadRequest(new { Message = "Webhooks can only post to text channels." });

            var name = NormalizeName(request.Name);
            if (name == null)
                return BadRequest(new { Message = "Webhook name must be 1-80 characters." });

            var avatarUrl = NormalizeOptional(request.AvatarUrl);
            if (!IsValidMediaUrl(avatarUrl))
                return BadRequest(new { Message = "Avatar must be blank, an http URL, or an uploaded file URL." });

            var token = GenerateToken();
            var now = DateTime.UtcNow;
            var webhook = new ServerWebhook
            {
                Id = Guid.NewGuid().ToString(),
                ServerId = request.ServerId,
                ChannelId = request.ChannelId,
                Name = name,
                AvatarUrl = avatarUrl,
                TokenHash = HashToken(token),
                CreatedBy = currentUsername,
                CreatedAt = now,
                TokenLastRotatedAt = now,
                IsEnabled = request.IsEnabled ?? true
            };

            _context.ServerWebhooks.Add(webhook);
            await _context.SaveChangesAsync();
            return Ok(BuildWebhookResponse(webhook, channel, token));
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> UpdateWebhook([FromBody] WebhookMutationRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            var webhook = await _context.ServerWebhooks.FirstOrDefaultAsync(item => item.Id == request.WebhookId);
            if (webhook == null)
                return NotFound(new { Message = "Webhook not found." });

            if (!await CanManageServer(webhook.ServerId, currentUsername))
                return Forbid();

            var channel = await _context.Channels.FirstOrDefaultAsync(item =>
                item.Id == request.ChannelId && item.ServerId == webhook.ServerId);
            if (channel == null)
                return NotFound(new { Message = "Channel not found." });
            if (channel.Type != "text")
                return BadRequest(new { Message = "Webhooks can only post to text channels." });

            var name = NormalizeName(request.Name);
            if (name == null)
                return BadRequest(new { Message = "Webhook name must be 1-80 characters." });

            var avatarUrl = NormalizeOptional(request.AvatarUrl);
            if (!IsValidMediaUrl(avatarUrl))
                return BadRequest(new { Message = "Avatar must be blank, an http URL, or an uploaded file URL." });

            webhook.ChannelId = channel.Id;
            webhook.Name = name;
            webhook.AvatarUrl = avatarUrl;
            webhook.IsEnabled = request.IsEnabled ?? webhook.IsEnabled;
            webhook.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(BuildWebhookResponse(webhook, channel));
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> DeleteWebhook([FromBody] WebhookActionRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            var webhook = await _context.ServerWebhooks.FirstOrDefaultAsync(item => item.Id == request.WebhookId);
            if (webhook == null)
                return NotFound(new { Message = "Webhook not found." });

            if (!await CanManageServer(webhook.ServerId, currentUsername))
                return Forbid();

            _context.ServerWebhooks.Remove(webhook);
            await _context.SaveChangesAsync();
            return Ok(new { Message = "Webhook deleted." });
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> RotateWebhookToken([FromBody] WebhookActionRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            var webhook = await _context.ServerWebhooks.FirstOrDefaultAsync(item => item.Id == request.WebhookId);
            if (webhook == null)
                return NotFound(new { Message = "Webhook not found." });

            if (!await CanManageServer(webhook.ServerId, currentUsername))
                return Forbid();

            var token = GenerateToken();
            webhook.TokenHash = HashToken(token);
            webhook.TokenLastRotatedAt = DateTime.UtcNow;
            webhook.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var channel = await _context.Channels.FirstOrDefaultAsync(item => item.Id == webhook.ChannelId);
            return Ok(BuildWebhookResponse(webhook, channel, token));
        }

        [AllowAnonymous]
        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> ExecuteWebhook(
            [FromBody] WebhookExecuteRequest request,
            string? webhookId = null,
            string? token = null)
        {
            webhookId = NormalizeOptional(webhookId ?? request.WebhookId);
            token = GetProvidedToken(token ?? request.Token, "X-Webhook-Token");
            if (string.IsNullOrWhiteSpace(webhookId) || string.IsNullOrWhiteSpace(token))
                return Unauthorized(new { Message = "Webhook id and token are required." });

            var tokenHash = HashToken(token);
            var webhook = await _context.ServerWebhooks.FirstOrDefaultAsync(item =>
                item.Id == webhookId && item.TokenHash == tokenHash);
            if (webhook == null || !webhook.IsEnabled)
                return Unauthorized(new { Message = "Webhook token is invalid." });

            var channel = await _context.Channels.FirstOrDefaultAsync(item =>
                item.Id == webhook.ChannelId && item.ServerId == webhook.ServerId);
            if (channel == null)
                return NotFound(new { Message = "Channel not found." });
            if (channel.Type != "text")
                return BadRequest(new { Message = "Webhooks can only post to text channels." });

            var displayName = NormalizeName(request.Username) ?? webhook.Name;
            var avatarUrl = NormalizeOptional(request.AvatarUrl) ?? webhook.AvatarUrl;
            if (!IsValidMediaUrl(avatarUrl))
                return BadRequest(new { Message = "Avatar must be blank, an http URL, or an uploaded file URL." });

            return await CreateIntegrationMessage(
                channel,
                webhook.Name,
                displayName,
                avatarUrl,
                request.Content,
                request.AttachmentUrl,
                request.AttachmentContentType,
                isBot: false,
                botAccountId: null,
                isWebhook: true,
                webhookId: webhook.Id,
                request.MessageId);
        }

        private async Task<IActionResult> CreateIntegrationMessage(
            Channel channel,
            string senderUsername,
            string senderDisplayName,
            string? senderAvatarUrl,
            string? text,
            string? attachmentUrl,
            string? attachmentContentType,
            bool isBot,
            string? botAccountId,
            bool isWebhook,
            string? webhookId,
            string? requestedMessageId)
        {
            var userText = text?.Trim() ?? string.Empty;
            attachmentUrl = NormalizeOptional(attachmentUrl);
            attachmentContentType = NormalizeOptional(attachmentContentType);

            if (!IsValidMessageBody(userText, attachmentUrl))
                return BadRequest(new { Message = "Message must be 1-4000 characters or include an attachment." });
            if (!IsValidAttachment(attachmentUrl))
                return BadRequest(new { Message = "Attachment must be blank, an http URL, or an uploaded file URL." });

            var messageId = string.IsNullOrWhiteSpace(requestedMessageId)
                ? Guid.NewGuid().ToString()
                : requestedMessageId.Trim();
            if (await _context.ServerMessages.AnyAsync(message => message.MessageID == messageId))
                return Conflict(new { Message = "Duplicate message id." });

            var message = new ServerMessage
            {
                MessageID = messageId,
                ChannelId = channel.Id,
                MessagesUserSender = senderUsername,
                Date = DateTime.UtcNow.ToString("O"),
                userText = userText,
                AttachmentUrl = attachmentUrl,
                AttachmentContentType = attachmentContentType,
                IsBot = isBot,
                BotAccountId = botAccountId,
                IsWebhook = isWebhook,
                WebhookId = webhookId,
                SenderDisplayName = senderDisplayName,
                SenderAvatarUrl = senderAvatarUrl
            };

            _context.ServerMessages.Add(message);
            await _context.SaveChangesAsync();
            return Ok(BuildMessageResponse(message));
        }

        private async Task<bool> CanBotSendToChannel(BotAccount bot, Channel channel)
        {
            var server = await _context.CreateServers.FirstOrDefaultAsync(item => item.ServerID == bot.ServerId);
            var roleName = NormalizeRoleName(bot.Role);
            var role = await _context.ServerRoles.FirstOrDefaultAsync(item =>
                item.ServerId == bot.ServerId && item.Name == roleName);
            role ??= BuildImplicitRole(bot.ServerId, roleName);
            var member = new ServerMember
            {
                ServerId = bot.ServerId,
                Username = bot.Username,
                Role = roleName
            };

            return ChannelPermissionPolicy.CanSendMessages(channel, server, member, role, bot.Username);
        }

        private async Task<bool> CanManageServer(string serverId, string username)
        {
            var server = await _context.CreateServers.FirstOrDefaultAsync(item => item.ServerID == serverId);
            if (server == null)
            {
                return false;
            }

            if (server.ServerOwner == username)
            {
                return true;
            }

            var member = await _context.ServerMembers.FirstOrDefaultAsync(item =>
                item.ServerId == serverId && item.Username == username);
            if (member == null)
            {
                return false;
            }

            var roleName = NormalizeRoleName(member.Role);
            var role = await _context.ServerRoles.FirstOrDefaultAsync(item =>
                item.ServerId == serverId && item.Name == roleName);
            role ??= BuildImplicitRole(serverId, roleName);
            if (role?.CanManageServer != true)
            {
                return false;
            }

            if (server.RequireTwoFactorForModerators && IsElevatedModeratorRole(role))
            {
                var account = await _context.Accounts.FirstOrDefaultAsync(account =>
                    account.UserName == username && !account.IsDisabled);
                return account?.TwoFactorEnabled == true;
            }

            return true;
        }

        private async Task<bool> IsIntegrationNameAvailable(string serverId, string name)
        {
            var normalizedName = name.ToLowerInvariant();
            var memberNameTaken = await _context.ServerMembers.AnyAsync(member =>
                member.ServerId == serverId && member.Username.ToLower() == normalizedName);
            if (memberNameTaken)
            {
                return false;
            }

            return !await _context.BotAccounts.AnyAsync(bot =>
                bot.ServerId == serverId &&
                (bot.DisplayName.ToLower() == normalizedName || bot.Username.ToLower() == normalizedName));
        }

        private async Task<string?> NormalizeAssignableRole(string serverId, string? requestedRole)
        {
            var roleName = NormalizeRoleName(requestedRole);
            if (!IsValidRoleName(roleName) || roleName == "owner")
            {
                return null;
            }

            if (IsBuiltInRole(roleName))
            {
                return roleName;
            }

            return await _context.ServerRoles.AnyAsync(role => role.ServerId == serverId && role.Name == roleName)
                ? roleName
                : null;
        }

        private object BuildBotResponse(BotAccount bot, string? token = null)
        {
            return new
            {
                bot.Id,
                bot.ServerId,
                bot.Username,
                bot.DisplayName,
                bot.AvatarUrl,
                bot.Description,
                Role = NormalizeRoleName(bot.Role),
                bot.CreatedBy,
                bot.CreatedAt,
                bot.UpdatedAt,
                bot.TokenLastRotatedAt,
                bot.IsEnabled,
                BotToken = token,
                AuthorizationHeader = token == null ? null : $"Bearer {token}"
            };
        }

        private object BuildWebhookResponse(ServerWebhook webhook, Channel? channel, string? token = null)
        {
            return new
            {
                webhook.Id,
                webhook.ServerId,
                webhook.ChannelId,
                ChannelName = channel?.Name,
                webhook.Name,
                webhook.AvatarUrl,
                webhook.CreatedBy,
                webhook.CreatedAt,
                webhook.UpdatedAt,
                webhook.TokenLastRotatedAt,
                webhook.IsEnabled,
                WebhookToken = token,
                Url = token == null ? null : BuildWebhookUrl(webhook.Id, token)
            };
        }

        private object BuildSlashCommandResponse(SlashCommand command, BotAccount? bot, bool canManage)
        {
            return new
            {
                command.Id,
                command.ServerId,
                command.BotAccountId,
                BotDisplayName = bot?.DisplayName,
                BotUsername = bot?.Username,
                BotAvatarUrl = bot?.AvatarUrl,
                BotEnabled = bot?.IsEnabled ?? false,
                command.Name,
                command.Description,
                command.Usage,
                command.CreatedBy,
                command.CreatedAt,
                command.UpdatedAt,
                command.IsEnabled,
                CanManage = canManage
            };
        }

        private static object BuildSlashInteractionResponse(SlashCommandInteraction interaction)
        {
            return new
            {
                interaction.Id,
                interaction.SlashCommandId,
                interaction.ServerId,
                interaction.ChannelId,
                interaction.BotAccountId,
                interaction.CommandName,
                interaction.InvokedBy,
                interaction.Arguments,
                interaction.CreatedAt,
                interaction.AcknowledgedAt,
                interaction.RespondedAt,
                interaction.ResponseMessageId,
                interaction.Status
            };
        }

        private async Task<BotAccount?> GetBotFromToken(string? requestToken)
        {
            var token = GetProvidedToken(requestToken, "X-Bot-Token");
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            var tokenHash = HashToken(token);
            return await _context.BotAccounts.FirstOrDefaultAsync(bot =>
                bot.TokenHash == tokenHash && bot.IsEnabled);
        }

        private async Task<bool> IsServerMember(string serverId, string username)
        {
            var server = await _context.CreateServers.FirstOrDefaultAsync(item => item.ServerID == serverId);
            if (server == null)
            {
                return false;
            }

            return server.ServerOwner == username ||
                   await _context.ServerMembers.AnyAsync(member =>
                       member.ServerId == serverId && member.Username == username);
        }

        private async Task<bool> CanUserSendToChannel(Channel channel, string username)
        {
            var server = await _context.CreateServers.FirstOrDefaultAsync(item => item.ServerID == channel.ServerId);
            var member = await _context.ServerMembers.FirstOrDefaultAsync(item =>
                item.ServerId == channel.ServerId && item.Username == username);
            var roleName = NormalizeRoleName(member?.Role);
            if (server?.ServerOwner == username)
            {
                roleName = "owner";
            }

            var role = await _context.ServerRoles.FirstOrDefaultAsync(item =>
                item.ServerId == channel.ServerId && item.Name == roleName);
            role ??= BuildImplicitRole(channel.ServerId, roleName);

            return ChannelPermissionPolicy.CanSendMessages(channel, server, member, role, username);
        }

        private static string? GetCommunicationRestriction(ServerMember? member)
        {
            if (member == null)
            {
                return null;
            }

            var now = DateTime.UtcNow;
            if (member.TimedOutUntil is { } timedOutUntil && timedOutUntil > now)
            {
                return $"You are timed out until {timedOutUntil:O}.";
            }

            if (member.IsMuted && (member.MutedUntil == null || member.MutedUntil > now))
            {
                return member.MutedUntil == null
                    ? "You are muted in this server."
                    : $"You are muted until {member.MutedUntil:O}.";
            }

            return null;
        }

        private static string? ExtractMessageId(object? value)
        {
            if (value == null)
            {
                return null;
            }

            var type = value.GetType();
            return type.GetProperty("MessageID")?.GetValue(value)?.ToString() ??
                   type.GetProperty("MessageId")?.GetValue(value)?.ToString();
        }

        private string BuildWebhookUrl(string webhookId, string token)
        {
            var request = HttpContext?.Request;
            var baseUrl = request == null || !request.Host.HasValue
                ? string.Empty
                : $"{request.Scheme}://{request.Host}";
            return $"{baseUrl}/api/ServerIntegrations/ExecuteWebhook?webhookId={Uri.EscapeDataString(webhookId)}&token={Uri.EscapeDataString(token)}";
        }

        private static object BuildMessageResponse(ServerMessage message)
        {
            return new
            {
                message.MessageID,
                message.ChannelId,
                message.MessagesUserSender,
                message.Date,
                message.userText,
                message.AttachmentUrl,
                message.AttachmentContentType,
                message.IsBot,
                message.BotAccountId,
                message.IsWebhook,
                message.WebhookId,
                message.SenderDisplayName,
                message.SenderAvatarUrl,
                Mentions = Array.Empty<string>(),
                Reactions = Array.Empty<object>()
            };
        }

        private string? GetProvidedToken(string? fallback, string headerName)
        {
            var authorization = Request?.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(authorization) &&
                authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeOptional(authorization["Bearer ".Length..]);
            }

            var headerValue = Request?.Headers[headerName].FirstOrDefault();
            return NormalizeOptional(headerValue) ?? NormalizeOptional(fallback);
        }

        private static string GenerateToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string HashToken(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string? NormalizeName(string? value)
        {
            var normalized = System.Text.RegularExpressions.Regex
                .Replace(value?.Trim() ?? string.Empty, @"\s+", " ");
            return normalized.Length is > 0 and <= MaxNameLength ? normalized : null;
        }

        private static string? NormalizeDescription(string? value)
        {
            var normalized = System.Text.RegularExpressions.Regex
                .Replace(value?.Trim() ?? string.Empty, @"\s+", " ");
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            return normalized.Length <= MaxDescriptionLength ? normalized : normalized[..MaxDescriptionLength];
        }

        private static string? NormalizeCommandName(string? value)
        {
            var normalized = (value ?? string.Empty).Trim().TrimStart('/').ToLowerInvariant();
            if (normalized.Length is <= 0 or > MaxCommandNameLength)
            {
                return null;
            }

            return System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^[a-z0-9][a-z0-9_-]*$")
                ? normalized
                : null;
        }

        private static string? NormalizeCommandDescription(string? value)
        {
            var normalized = System.Text.RegularExpressions.Regex
                .Replace(value?.Trim() ?? string.Empty, @"\s+", " ");
            return normalized.Length is > 0 and <= MaxCommandDescriptionLength ? normalized : null;
        }

        private static string? NormalizeCommandUsage(string? value, string name)
        {
            var normalized = System.Text.RegularExpressions.Regex
                .Replace(value?.Trim() ?? string.Empty, @"\s+", " ");
            if (string.IsNullOrWhiteSpace(normalized) || normalized == "/")
            {
                normalized = $"/{name}";
            }
            else if (!normalized.StartsWith("/", StringComparison.Ordinal))
            {
                normalized = $"/{normalized}";
            }

            return normalized.Length <= MaxCommandUsageLength ? normalized : null;
        }

        private static string? NormalizeInteractionArguments(string? value)
        {
            var normalized = value?.Trim() ?? string.Empty;
            return normalized.Length <= MaxInteractionArgumentsLength ? normalized : null;
        }

        private static string? NormalizeOptional(string? value)
        {
            var normalized = value?.Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private static string NormalizeRoleName(string? value)
        {
            var normalized = (value ?? "user").Trim().ToLowerInvariant().Replace(' ', '-');
            return string.IsNullOrWhiteSpace(normalized) ? "user" : normalized;
        }

        private static bool IsValidRoleName(string name)
        {
            return name.Length is > 0 and <= 40 &&
                   name.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');
        }

        private static bool IsBuiltInRole(string roleName)
        {
            return roleName is "owner" or "admin" or "moderator" or "user";
        }

        private static ServerRole? BuildImplicitRole(string serverId, string roleName)
        {
            return roleName switch
            {
                "owner" => new ServerRole
                {
                    ServerId = serverId,
                    Name = "owner",
                    CanManageServer = true,
                    CanManageChannels = true,
                    CanManageMembers = true,
                    CanBanMembers = true,
                    CanCreateInvites = true,
                    CanSendMessages = true,
                    CanJoinVoice = true
                },
                "admin" => new ServerRole
                {
                    ServerId = serverId,
                    Name = "admin",
                    CanManageServer = true,
                    CanManageChannels = true,
                    CanManageMembers = true,
                    CanBanMembers = true,
                    CanCreateInvites = true,
                    CanSendMessages = true,
                    CanJoinVoice = true
                },
                "moderator" => new ServerRole
                {
                    ServerId = serverId,
                    Name = "moderator",
                    CanManageChannels = true,
                    CanManageMembers = true,
                    CanBanMembers = true,
                    CanCreateInvites = true,
                    CanSendMessages = true,
                    CanJoinVoice = true
                },
                "user" => new ServerRole
                {
                    ServerId = serverId,
                    Name = "user",
                    CanCreateInvites = true,
                    CanSendMessages = true,
                    CanJoinVoice = true
                },
                _ => null
            };
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

        private static bool IsValidMessageBody(string? message, string? attachmentUrl)
        {
            return (!string.IsNullOrWhiteSpace(message) && message.Length <= MaxMessageLength) ||
                   !string.IsNullOrWhiteSpace(attachmentUrl);
        }

        private static bool IsValidAttachment(string? attachmentUrl)
        {
            if (string.IsNullOrWhiteSpace(attachmentUrl))
            {
                return true;
            }

            if (attachmentUrl.StartsWith("/uploads/", StringComparison.Ordinal))
            {
                return true;
            }

            return Uri.TryCreate(attachmentUrl, UriKind.Absolute, out var parsedUrl) &&
                   (parsedUrl.Scheme == Uri.UriSchemeHttp || parsedUrl.Scheme == Uri.UriSchemeHttps);
        }

        private static bool IsValidMediaUrl(string? url)
        {
            return IsValidAttachment(url);
        }
    }

    public class BotAccountMutationRequest
    {
        public string? BotId { get; set; }
        public string ServerId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public string? Description { get; set; }
        public string Role { get; set; } = "user";
        public bool? IsEnabled { get; set; }
    }

    public class BotAccountActionRequest
    {
        public string BotId { get; set; } = string.Empty;
    }

    public class BotMessageRequest
    {
        public string? Token { get; set; }
        public string? MessageId { get; set; }
        public string ChannelId { get; set; } = string.Empty;
        public string UserText { get; set; } = string.Empty;
        public string? AttachmentUrl { get; set; }
        public string? AttachmentContentType { get; set; }
    }

    public class SlashCommandMutationRequest
    {
        public string? CommandId { get; set; }
        public string ServerId { get; set; } = string.Empty;
        public string BotAccountId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? Usage { get; set; }
        public bool? IsEnabled { get; set; }
    }

    public class SlashCommandActionRequest
    {
        public string CommandId { get; set; } = string.Empty;
    }

    public class BotSlashCommandRegistrationRequest
    {
        public string? Token { get; set; }
        public string? CommandId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? Usage { get; set; }
        public bool? IsEnabled { get; set; }
    }

    public class BotSlashCommandListRequest
    {
        public string? Token { get; set; }
    }

    public class SlashCommandExecuteRequest
    {
        public string? CommandId { get; set; }
        public string ServerId { get; set; } = string.Empty;
        public string ChannelId { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Arguments { get; set; }
    }

    public class BotPendingSlashCommandRequest
    {
        public string? Token { get; set; }
        public int Take { get; set; } = 25;
    }

    public class SlashCommandInteractionActionRequest
    {
        public string? Token { get; set; }
        public string InteractionId { get; set; } = string.Empty;
    }

    public class SlashCommandResponseRequest
    {
        public string? Token { get; set; }
        public string InteractionId { get; set; } = string.Empty;
        public string? MessageId { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? AttachmentUrl { get; set; }
        public string? AttachmentContentType { get; set; }
    }

    public class WebhookMutationRequest
    {
        public string? WebhookId { get; set; }
        public string ServerId { get; set; } = string.Empty;
        public string ChannelId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public bool? IsEnabled { get; set; }
    }

    public class WebhookActionRequest
    {
        public string WebhookId { get; set; } = string.Empty;
    }

    public class WebhookExecuteRequest
    {
        public string? WebhookId { get; set; }
        public string? Token { get; set; }
        public string? MessageId { get; set; }
        public string? Username { get; set; }
        public string? AvatarUrl { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? AttachmentUrl { get; set; }
        public string? AttachmentContentType { get; set; }
    }
}
