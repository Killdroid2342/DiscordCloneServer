using DiscordCloneServer.Data;
using Microsoft.AspNetCore.Mvc;

namespace DiscordCloneServer.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class ServerMessagesController : ControllerBase
    {
        private readonly ApiContext _context;
        private readonly IConfiguration _config;
        public ServerMessagesController(ApiContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }
        [HttpPost]
        public JsonResult ServerMessages(Models.ServerMessage serverMessage)
        {

            _context.ServerMessages.Add(serverMessage);

            _context.SaveChanges();
            return new JsonResult(serverMessage);
        }

        [HttpGet]
        public JsonResult GetServerMessages(string channelId)
        {
            try
            {
                var messages = _context.ServerMessages.Where(msg => msg.ChannelId == channelId).ToList();
                return new JsonResult(messages);
            }
            catch (Exception ex)
            {
                return new JsonResult($"Internal server error: {ex.Message}");
            }
        }
    }
}
