using DiscordCloneServer.Data;
using DiscordCloneServer.Migrations;
using DiscordCloneServer.Models;
using Microsoft.AspNetCore.Mvc;

namespace DiscordCloneServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ServerController : ControllerBase
    {
        private readonly ApiContext _context;
        private readonly IConfiguration _config;
        public ServerController(ApiContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }
        // create/edit
        [HttpPost]
        public JsonResult CreateServer(Models.CreateServer createServer)
        {
            Console.WriteLine("this line was executed ");

            _context.CreateServers.Add(createServer);

            return new JsonResult(createServer);
        }
    }

}
