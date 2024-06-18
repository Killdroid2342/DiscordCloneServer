using DiscordCloneServer.Data;
using Microsoft.AspNetCore.Mvc;

namespace DiscordCloneServer.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class PrivateMessageFriendController : ControllerBase
    {
        private readonly ApiContext _context;
        private readonly IConfiguration _config;
        public PrivateMessageFriendController(ApiContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }
        [HttpPost]
        public JsonResult SendPrivateMessage(Models.PrivateMessageFriend privateMessageFriend)
        {
            if (_context.PrivateMessageFriends.Any(pm => pm.PrivateMessageID == privateMessageFriend.PrivateMessageID))
            {
                return new JsonResult(new { error = "Duplicate PrivateMessageID" });
            }
            _context.PrivateMessageFriends.Add(privateMessageFriend);
            _context.SaveChanges();
            return new JsonResult(privateMessageFriend);
        }
        [HttpGet]
        public JsonResult GetPrivateMessage(string currentUsername, string targetUsername)
        {
            var messages = _context.PrivateMessageFriends
                .Where(pm => (pm.MessagesUserSender == currentUsername && pm.MessageUserReciver == targetUsername) ||
                             (pm.MessagesUserSender == targetUsername && pm.MessageUserReciver == currentUsername))
                .ToList();

            return new JsonResult(messages);
        }
    }
}
