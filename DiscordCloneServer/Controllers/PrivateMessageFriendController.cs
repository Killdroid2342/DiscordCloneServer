using DiscordCloneServer.Data;
using Microsoft.AspNetCore.Mvc;

namespace DiscordCloneServer.Controllers
{
    [Route("api/[controller]")]
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

            _context.PrivateMessageFriends.Add(privateMessageFriend);

            _context.SaveChanges();
            return new JsonResult(privateMessageFriend);
        }
    }
}
