using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

        private static ConcurrentDictionary<string, WebSocket> _activeSockets = new ConcurrentDictionary<string, WebSocket>();

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
                .AsEnumerable() 
                .OrderBy(pm => 
                {
                    DateTime dt;
                    if (DateTime.TryParse(pm.Date, out dt)) return dt;
                    return DateTime.MinValue;
                })
                .ToList();

            return new JsonResult(messages);
        }
        [HttpGet]
        public async Task HandlePrivateWebsocket(string username)
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
                    var privateMessage = Newtonsoft.Json.JsonConvert.DeserializeObject<Models.PrivateMessageFriend>(messageJson);

                    _context.PrivateMessageFriends.Add(privateMessage);
                    _context.SaveChanges();

                    if (_activeSockets.TryGetValue(privateMessage.MessageUserReciver, out WebSocket receiverSocket) &&
                        receiverSocket.State == WebSocketState.Open)
                    {
                        var messageBuffer = Encoding.UTF8.GetBytes(messageJson);
                        await receiverSocket.SendAsync(new ArraySegment<byte>(messageBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
                }
            }
        }

    }
}
