using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DiscordCloneServer.Data;
using DiscordCloneServer.Models;
using Microsoft.AspNetCore.Mvc;

namespace DiscordCloneServer.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class GroupChatController : ControllerBase
    {
        private readonly ApiContext _context;
        private static ConcurrentDictionary<string, WebSocket> _activeSockets = new ConcurrentDictionary<string, WebSocket>();

        public GroupChatController(ApiContext context)
        {
            _context = context;
        }

        [HttpPost]
        public JsonResult CreateGroup([FromBody] CreateGroupRequest request)
        {
            try
            {
                var group = new GroupChat
                {
                    Id = Guid.NewGuid(),
                    Name = request.Name,
                    Owner = request.Owner,
                    Members = request.Members
                };

                _context.GroupChats.Add(group);

               
                foreach (var memberName in request.Members)
                {
                    var user = _context.Accounts.FirstOrDefault(a => a.UserName == memberName);
                    if (user != null)
                    {
                        var groups = user.Groups?.ToList() ?? new List<string>();
                        groups.Add(group.Id.ToString());
                        user.Groups = groups.ToArray();
                    }
                }

                _context.SaveChanges();
                return new JsonResult(group);
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }

        [HttpGet]
        public JsonResult GetGroups(string username)
        {
            var user = _context.Accounts.FirstOrDefault(a => a.UserName == username);
            if (user == null || user.Groups == null) return new JsonResult(new List<GroupChat>());

            var groupIds = user.Groups.Select(g => Guid.Parse(g)).ToList();
            var groups = _context.GroupChats.Where(g => groupIds.Contains(g.Id)).ToList();
            return new JsonResult(groups);
        }

        [HttpGet]
        public JsonResult GetGroupMessages(Guid groupId)
        {
            var messages = _context.GroupMessages
                .Where(m => m.GroupId == groupId)
                .AsEnumerable()
                .OrderBy(m => 
                {
                    if (DateTime.TryParse(m.Date, out var dt)) return dt;
           
                     if (DateTime.TryParseExact(m.Date, "dd/MM/yyyy, HH:mm:ss", 
                        System.Globalization.CultureInfo.InvariantCulture, 
                        System.Globalization.DateTimeStyles.None, out var dt2)) return dt2;
                    return DateTime.MinValue; 
                })
                .ToList();
            return new JsonResult(messages);
        }

  
        [HttpGet]
        public async Task HandleGroupWebsocket(string username)
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                WebSocket webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
                _activeSockets[username] = webSocket;
                await ListenForMessages(username, webSocket);
                _activeSockets.TryRemove(username, out _);
            }
            else
            {
                HttpContext.Response.StatusCode = 400;
            }
        }

        private async Task ListenForMessages(string username, WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string messageJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    
                    using (JsonDocument doc = JsonDocument.Parse(messageJson))
                    {
                        JsonElement root = doc.RootElement;
                        string type = root.TryGetProperty("Type", out var typeProp) ? typeProp.GetString() : "chat";

                        if (type == "chat")
                        {
                            var groupMessage = System.Text.Json.JsonSerializer.Deserialize<GroupMessage>(messageJson);
                            if (groupMessage != null)
                            {
                                groupMessage.Id = Guid.NewGuid();
                                _context.GroupMessages.Add(groupMessage);
                                _context.SaveChanges();

                                var group = _context.GroupChats.FirstOrDefault(g => g.Id == groupMessage.GroupId);
                                if (group != null)
                                {
                                    foreach (var member in group.Members.Distinct())
                                    {
                                        if (_activeSockets.TryGetValue(member, out WebSocket socket) && socket.State == WebSocketState.Open)
                                        {
                                            var msgBuffer = Encoding.UTF8.GetBytes(messageJson); 
                                            await socket.SendAsync(new ArraySegment<byte>(msgBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
                                        }
                                    }
                                }
                            }
                        }
                        else 
                        {
                            string targetUser = root.TryGetProperty("TargetUser", out var targetProp) ? targetProp.GetString() : null;
                            string groupIdStr = root.TryGetProperty("GroupId", out var groupProp) ? groupProp.GetString() : null;

                            if (!string.IsNullOrEmpty(targetUser))
                            {
                                if (_activeSockets.TryGetValue(targetUser, out WebSocket socket) && socket.State == WebSocketState.Open)
                                {
                                     var msgBuffer = Encoding.UTF8.GetBytes(messageJson);
                                     await socket.SendAsync(new ArraySegment<byte>(msgBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
                                }
                            }
                            else if (!string.IsNullOrEmpty(groupIdStr) && Guid.TryParse(groupIdStr, out var groupId))
                            {
                                var group = _context.GroupChats.FirstOrDefault(g => g.Id == groupId);
                                if (group != null)
                                {
                                    foreach (var member in group.Members.Distinct())
                                    {
                                         if (_activeSockets.TryGetValue(member, out WebSocket socket) && socket.State == WebSocketState.Open)
                                        {
                                            var msgBuffer = Encoding.UTF8.GetBytes(messageJson);
                                            await socket.SendAsync(new ArraySegment<byte>(msgBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
                }
            }
        }
    }

    public class CreateGroupRequest
    {
        public string Name { get; set; }
        public string Owner { get; set; }
        public string[] Members { get; set; }
    }
}
