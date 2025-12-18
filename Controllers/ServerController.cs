using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DiscordCloneServer.Data;
using DiscordCloneServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;

using DiscordCloneServer.Hubs;

namespace DiscordCloneServer.Controllers
{
    public class ServerIdComparer : IEqualityComparer<CreateServer>
    {
        public bool Equals(CreateServer x, CreateServer y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return x.ServerID == y.ServerID;
        }

        public int GetHashCode([DisallowNull] CreateServer obj)
        {
            return obj.ServerID.GetHashCode();
        }
    }

    [Route("api/[controller]/[action]")]
    [ApiController]
    public class ServerController : ControllerBase
    {
        private readonly ApiContext _context;
        private readonly IConfiguration _config;
        private readonly IHubContext<ChatHub> _hubContext;

        public ServerController(ApiContext context, IConfiguration config, IHubContext<ChatHub> hubContext)
        {
            _context = context;
            _config = config;
            _hubContext = hubContext;
        }


        [HttpPost]
        public async Task<IActionResult> CreateServer([FromBody] CreateServer createServer)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            Console.WriteLine($"making new server: '{createServer.ServerName}' by {createServer.ServerOwner}");

            if (string.IsNullOrWhiteSpace(createServer.ServerName))
                return BadRequest(new { Message = "Server name is required." });

            createServer.ServerID = Guid.NewGuid().ToString();
            createServer.InviteLink = $"https://localhost:7170/invite/{Guid.NewGuid()}";
            createServer.Date = DateTime.UtcNow;

            _context.CreateServers.Add(createServer);
            await _context.SaveChangesAsync();

            var ownerMembership = new ServerMember
            {
                Id = Guid.NewGuid().ToString(),
                ServerId = createServer.ServerID,
                Username = createServer.ServerOwner,
                Role = "owner"
            };

            _context.ServerMembers.Add(ownerMembership);
            await _context.SaveChangesAsync();

         
            var textCategory = new Category { Id = Guid.NewGuid().ToString(), ServerId = createServer.ServerID, Name = "Text Channels" };
            var voiceCategory = new Category { Id = Guid.NewGuid().ToString(), ServerId = createServer.ServerID, Name = "Voice Channels" };

            _context.Categories.AddRange(textCategory, voiceCategory);

           
            var generalText = new Channel { Id = Guid.NewGuid().ToString(), ServerId = createServer.ServerID, CategoryId = textCategory.Id, Name = "general", Type = "text" };
            var generalVoice = new Channel { Id = Guid.NewGuid().ToString(), ServerId = createServer.ServerID, CategoryId = voiceCategory.Id, Name = "General", Type = "voice" };

            _context.Channels.AddRange(generalText, generalVoice);
            await _context.SaveChangesAsync();

            return Ok(createServer);
        }


        [HttpGet]
        public async Task<IActionResult> GetServer(string username)
        {
            var memberships = await _context.ServerMembers
                .Where(member => member.Username == username)
                .ToListAsync();

            var memberServerIds = memberships
                .Select(member => member.ServerId)
                .ToList();

            var servers = await _context.CreateServers
                .Where(server => memberServerIds.Contains(server.ServerID) || server.ServerOwner == username)
                .ToListAsync();

            if (servers.Any())
            {
                var serverResponse = servers.Select(server =>
                {
                    var membership = memberships.FirstOrDefault(m => m.ServerId == server.ServerID);
                    string role = "user";

                    if (membership != null)
                    {
                        role = membership.Role;
                    }
                    else if (server.ServerOwner == username)
                    {
                        role = "owner";
                    }

                    return new
                    {
                        server.ServerID,
                        server.ServerName,
                        server.ServerOwner,
                        server.InviteLink,
                        server.Date,
                        Role = role
                    };
                }).ToList();

                return new JsonResult(
                    serverResponse,
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
                Username = req.Username,
                Role = "user"
            };

            _context.ServerMembers.Add(membership);
            await _context.SaveChangesAsync();


            await _hubContext.Clients.Group(server.ServerID).SendAsync("NewMember", req.Username);

            return Ok(server);
        }


        [HttpGet]
        public async Task<IActionResult> GetServerDetails(string serverId)
        {
            var categories = await _context.Categories.Where(c => c.ServerId == serverId).ToListAsync();
            var channels = await _context.Channels.Where(c => c.ServerId == serverId).ToListAsync();

            return Ok(new
            {
                Categories = categories,
                Channels = channels
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetServerMembers(string serverId)
        {
            var members = await _context.ServerMembers
                .Where(m => m.ServerId == serverId)
                .Select(m => new
                {
                    m.Id,
                    m.Username,
                    m.Role
                })
                .ToListAsync();

            return Ok(members);
        }

    }
}
