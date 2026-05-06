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
    [Authorize]
    public class PollsController : ControllerBase
    {
        private readonly ApiContext _context;

        public PollsController(ApiContext context)
        {
            _context = context;
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> Vote([FromBody] MessagePollVoteRequest request)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            var poll = await ResolvePoll(request);
            if (poll == null)
                return NotFound(new { message = "Poll not found." });

            if (!await CanAccessPoll(poll, username))
                return Forbid();

            if (poll.ExpiresAt.HasValue && poll.ExpiresAt.Value <= DateTime.UtcNow)
                return BadRequest(new { message = "This poll is closed." });

            var selectedOptionIds = (request.OptionIds ?? Array.Empty<string>())
                .Append(request.OptionId ?? string.Empty)
                .Select(optionId => optionId.Trim())
                .Where(optionId => !string.IsNullOrWhiteSpace(optionId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (selectedOptionIds.Length == 0)
                return BadRequest(new { message = "Choose at least one poll option." });

            if (!poll.AllowMultiple && selectedOptionIds.Length != 1)
                return BadRequest(new { message = "This poll only allows one option." });

            var options = await _context.MessagePollOptions
                .Where(option => option.PollId == poll.Id)
                .ToListAsync();
            var validOptionIds = options.Select(option => option.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (selectedOptionIds.Any(optionId => !validOptionIds.Contains(optionId)))
                return BadRequest(new { message = "One or more poll options are invalid." });

            var existingVotes = await _context.MessagePollVotes
                .Where(vote => vote.PollId == poll.Id && vote.Username == username)
                .ToListAsync();
            _context.MessagePollVotes.RemoveRange(existingVotes);

            foreach (var optionId in selectedOptionIds)
            {
                _context.MessagePollVotes.Add(new MessagePollVote
                {
                    Id = Guid.NewGuid().ToString(),
                    PollId = poll.Id,
                    OptionId = optionId,
                    Username = username,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
            return Ok(await MessagePollService.GetPollResponseAsync(_context, poll, username));
        }

        private async Task<MessagePoll?> ResolvePoll(MessagePollVoteRequest request)
        {
            var pollId = request.PollId?.Trim();
            if (!string.IsNullOrWhiteSpace(pollId))
            {
                return await _context.MessagePolls.FirstOrDefaultAsync(poll => poll.Id == pollId);
            }

            var scopeType = request.ScopeType?.Trim().ToLowerInvariant();
            var messageId = request.MessageId?.Trim();
            if (string.IsNullOrWhiteSpace(scopeType) || string.IsNullOrWhiteSpace(messageId))
            {
                return null;
            }

            return await _context.MessagePolls.FirstOrDefaultAsync(poll =>
                poll.ScopeType == scopeType && poll.MessageId == messageId);
        }

        private async Task<bool> CanAccessPoll(MessagePoll poll, string username)
        {
            if (poll.ScopeType == "server")
            {
                var message = await _context.ServerMessages.FirstOrDefaultAsync(item => item.MessageID == poll.MessageId);
                if (message == null)
                    return false;

                var channel = await _context.Channels.FirstOrDefaultAsync(item => item.Id == message.ChannelId);
                return channel != null && await _context.ServerMembers.AnyAsync(member =>
                    member.ServerId == channel.ServerId &&
                    member.Username == username);
            }

            if (poll.ScopeType == "dm")
            {
                var message = await _context.PrivateMessageFriends.FirstOrDefaultAsync(item =>
                    item.PrivateMessageID == poll.MessageId);
                return message != null &&
                       (message.MessagesUserSender == username || message.MessageUserReciver == username);
            }

            if (poll.ScopeType == "group")
            {
                if (!Guid.TryParse(poll.MessageId, out var groupMessageId))
                    return false;

                var message = await _context.GroupMessages.FirstOrDefaultAsync(item => item.Id == groupMessageId);
                if (message == null)
                    return false;

                var group = await _context.GroupChats.FirstOrDefaultAsync(item => item.Id == message.GroupId);
                return group?.Members.Any(member =>
                    string.Equals(member, username, StringComparison.OrdinalIgnoreCase)) == true;
            }

            return false;
        }
    }

    public class MessagePollVoteRequest
    {
        public string? PollId { get; set; }
        public string? ScopeType { get; set; }
        public string? MessageId { get; set; }
        public string? OptionId { get; set; }
        public string[]? OptionIds { get; set; }
    }
}
