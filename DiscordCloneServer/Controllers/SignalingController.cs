using System.Collections.Concurrent;
using DiscordCloneServer.Models;
using Microsoft.AspNetCore.Mvc;

namespace DiscordCloneServer.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class SignalingController : ControllerBase
    {
        private static ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentQueue<SignalMessage>>> serverMessages
            = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentQueue<SignalMessage>>>();

        private static ConcurrentDictionary<string, HashSet<string>> serverUsers
            = new ConcurrentDictionary<string, HashSet<string>>();

        [HttpPost]
        public IActionResult SendMessage([FromBody] SignalMessage message, string serverId, string toUser)
        {
            var server = serverMessages.GetOrAdd(serverId, _ => new ConcurrentDictionary<string, ConcurrentQueue<SignalMessage>>());
            var queue = server.GetOrAdd(toUser, _ => new ConcurrentQueue<SignalMessage>());
            queue.Enqueue(message);
            return Ok();
        }

        [HttpGet]
        public IActionResult ReceiveMessages(string serverId, string username)
        {
            if (serverMessages.TryGetValue(serverId, out var users) &&
                users.TryGetValue(username, out var queue))
            {
                var messages = queue.ToArray();
                queue.Clear();
                return Ok(messages);
            }
            return Ok(new List<SignalMessage>());
        }

        [HttpPost]
        public IActionResult JoinVoice(string serverId, string username)
        {
            var users = serverUsers.GetOrAdd(serverId, _ => new HashSet<string>());
            lock (users)
            {
                users.Add(username);
            }
            return Ok(users.ToList());
        }

        [HttpPost]
        public IActionResult LeaveVoice(string serverId, string username)
        {
            if (serverUsers.TryGetValue(serverId, out var users))
            {
                lock (users)
                {
                    users.Remove(username);
                }
            }
            return Ok();
        }


        [HttpGet]
        public IActionResult GetActiveUsers(string serverId)
        {
            if (serverUsers.TryGetValue(serverId, out var users))
            {
                lock (users)
                {
                    return Ok(users.ToList());
                }
            }
            return Ok(new List<string>());
        }
    }
}
