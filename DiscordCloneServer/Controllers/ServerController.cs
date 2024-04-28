using System.Text.Json;
using DiscordCloneServer.Data;
using DiscordCloneServer.Migrations;
using DiscordCloneServer.Models;
using Microsoft.AspNetCore.Mvc;

namespace DiscordCloneServer.Controllers
{
    [Route("api/[controller]/[action]")]
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

            _context.SaveChanges();
            return new JsonResult(createServer);
        }

        [HttpGet]
        public JsonResult GetServer(string username)
        {
            var servers = _context.CreateServers
                                .Where(server => server.ServerOwner == username)
                                .ToList();

            if (servers.Any())
            {
                return new JsonResult(servers, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }
            else
            {
                return new JsonResult(new { Message = "No servers here" }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }
        }
    }

}

