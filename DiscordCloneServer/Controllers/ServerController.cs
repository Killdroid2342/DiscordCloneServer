using System.Text.Json;
using DiscordCloneServer.Data;
using DiscordCloneServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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


        [HttpPost]
        public async Task<IActionResult> CreateServer([FromBody] CreateServer createServer)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            Console.WriteLine($"📥 Incoming CreateServer: Name={createServer.ServerName}, Owner={createServer.ServerOwner}");

            if (string.IsNullOrWhiteSpace(createServer.ServerName))
                return BadRequest(new { Message = "Server name is required." });

            createServer.ServerID = Guid.NewGuid().ToString();
            createServer.InviteLink = $"https://localhost:7170/invite/{Guid.NewGuid()}";
            createServer.Date = DateTime.UtcNow;

            _context.CreateServers.Add(createServer);
            await _context.SaveChangesAsync();

            return Ok(createServer);
        }


        [HttpGet]
        public async Task<IActionResult> GetServer(string username)
        {
            var servers = await _context.CreateServers
                .Where(server => server.ServerOwner == username)
                .ToListAsync();

            if (servers.Any())
            {
                return new JsonResult(
                    servers,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
                );
            }
            else
            {
                return new JsonResult(
                    new { Message = "No servers here" },
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
                );
            }
        }


        [HttpGet]
        public async Task<IActionResult> GetInviteLink(string serverId)
        {
            var server = await _context.CreateServers
                .FirstOrDefaultAsync(s => s.ServerID == serverId);

            if (server == null)
                return NotFound(new { Message = "Server not found" });

            if (string.IsNullOrEmpty(server.InviteLink))
            {
                server.InviteLink = $"https://localhost:7170/invite/{Guid.NewGuid()}";
                _context.CreateServers.Update(server);
                await _context.SaveChangesAsync();
            }

            return Ok(new { InviteLink = server.InviteLink });
        }


        [HttpPost]
        public async Task<IActionResult> JoinServer([FromBody] JoinServerRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.InviteLink))
                return BadRequest(new { Message = "Invite link is required." });

            var server = await _context.CreateServers
                .FirstOrDefaultAsync(s => s.InviteLink == req.InviteLink);

            if (server == null)
                return NotFound(new { Message = "Server not found" });

            var membership = new ServerMember
            {
                Id = Guid.NewGuid().ToString(),
                ServerId = server.ServerID,
                Username = req.Username
            };

            _context.ServerMembers.Add(membership);
            await _context.SaveChangesAsync();

            return Ok(server);
        }

    }
}
