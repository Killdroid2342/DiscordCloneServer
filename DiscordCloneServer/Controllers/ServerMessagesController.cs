using System.Text.Json;
using DiscordCloneServer.Data;
using DiscordCloneServer.Models;
using Microsoft.AspNetCore.Mvc;

namespace DiscordCloneServer.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class ServerMessagesController : Controller
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
        public JsonResult GetServerMessages(string username)
        {
            return new JsonResult(username);
        }
    }
}
