using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace DiscordCloneServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class VoiceWebSocketController : ControllerBase
    {
        private static readonly ConcurrentDictionary<string, HashSet<WebSocketConnection>> VoiceServerConnections = new();
        private static readonly ConcurrentDictionary<string, WebSocketConnection> UserConnections = new();
        private static readonly ConcurrentDictionary<string, string> ConnectionToUser = new();
        private static readonly ConcurrentDictionary<string, string> UserToServer = new();

        [HttpGet("/voice-ws")]
        public async Task HandleWebSocket()
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
                var connectionId = Guid.NewGuid().ToString();
                var connection = new WebSocketConnection(connectionId, webSocket);

                await HandleWebSocketConnection(connection);
            }
            else
            {
                HttpContext.Response.StatusCode = 400;
            }
        }

        private async Task HandleWebSocketConnection(WebSocketConnection connection)
        {
            var buffer = new byte[1024 * 4];

            try
            {
                while (connection.WebSocket.State == WebSocketState.Open)
                {
                    using (var ms = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await connection.WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                            ms.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);

                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            ms.Seek(0, SeekOrigin.Begin);
                            using (var reader = new StreamReader(ms, Encoding.UTF8))
                            {
                                var message = await reader.ReadToEndAsync();
                                await ProcessMessage(connection, message);
                            }
                        }
                        else if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await HandleDisconnection(connection);
                            break;
                        }
                    }

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"voice connection broke: {ex.Message}");
                await HandleDisconnection(connection);
            }
        }

        private async Task ProcessMessage(WebSocketConnection connection, string message)
        {
            Console.WriteLine($"[WS] RX: {message}");
            try
            {
                var messageObj = JsonSerializer.Deserialize<WebSocketMessage>(message);
                if (messageObj == null) return;

                switch (messageObj.Type)
                {
                    case "join":
                        if (messageObj.ServerId != null && messageObj.Username != null)
                            await HandleJoinVoice(connection, messageObj.ServerId, messageObj.Username);
                        break;
                    case "leave":
                        if (messageObj.ServerId != null && messageObj.Username != null)
                            await HandleLeaveVoice(connection, messageObj.ServerId, messageObj.Username);
                        break;
                    case "offer":
                        if (messageObj.TargetUser != null && messageObj.Data != null)
                            await HandleOffer(connection, messageObj.TargetUser, messageObj.Data);
                        break;
                    case "answer":
                        if (messageObj.TargetUser != null && messageObj.Data != null)
                            await HandleAnswer(connection, messageObj.TargetUser, messageObj.Data);
                        break;
                    case "ice-candidate":
                        if (messageObj.TargetUser != null && messageObj.Data != null)
                            await HandleIceCandidate(connection, messageObj.TargetUser, messageObj.Data);
                        break;
                    case "peer-offer":
                        if (messageObj.TargetUser != null && messageObj.Data != null)
                            await HandlePeerOffer(connection, messageObj.TargetUser, messageObj.Data);
                        break;
                    case "peer-answer":
                        if (messageObj.TargetUser != null && messageObj.Data != null)
                            await HandlePeerAnswer(connection, messageObj.TargetUser, messageObj.Data);
                        break;
                    case "peer-ice-candidate":
                        if (messageObj.TargetUser != null && messageObj.Data != null)
                            await HandlePeerIceCandidate(connection, messageObj.TargetUser, messageObj.Data);
                        break;
                    case "server-offer":
                        if (messageObj.Data != null)
                            await HandleServerOffer(connection, messageObj.Data);
                        break;
                    case "server-answer":
                        if (messageObj.Data != null)
                            await HandleServerAnswer(connection, messageObj.Data);
                        break;
                    case "server-ice-candidate":
                        if (messageObj.Data != null)
                            await HandleServerIceCandidate(connection, messageObj.Data);
                        break;
                    case "audio-data":
                        if (messageObj.Data != null)
                            await HandleAudioData(connection, messageObj.Data);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"couldnt process voice msg: {ex.Message}");
            }
        }

        private async Task HandleJoinVoice(WebSocketConnection connection, string serverId, string username)
        {
            ConnectionToUser[connection.Id] = username;
            UserConnections[username] = connection;
            UserToServer[username] = serverId;

            var connections = VoiceServerConnections.GetOrAdd(serverId, _ => new HashSet<WebSocketConnection>());
            List<string> existingUsers;

            lock (connections)
            {
                existingUsers = connections.Where(c => ConnectionToUser.ContainsKey(c.Id))
                                         .Select(c => ConnectionToUser[c.Id])
                                         .ToList();
                connections.Add(connection);
            }

            await BroadcastToServer(serverId, new WebSocketMessage
            {
                Type = "user-joined",
                Username = username
            }, connection.Id);


            await SendToConnection(connection, new WebSocketMessage
            {
                Type = "existing-users",
                Data = JsonSerializer.Serialize(existingUsers)
            });


            var allUsers = existingUsers.Concat(new[] { username }).ToList();
            await BroadcastToServer(serverId, new WebSocketMessage
            {
                Type = "users-updated",
                Data = JsonSerializer.Serialize(allUsers)
            });
        }

        private async Task HandleLeaveVoice(WebSocketConnection connection, string serverId, string username)
        {
            if (VoiceServerConnections.TryGetValue(serverId, out var connections))
            {
                lock (connections)
                {
                    connections.Remove(connection);
                }


                await BroadcastToServer(serverId, new WebSocketMessage
                {
                    Type = "user-left",
                    Username = username
                }, connection.Id);


                var remainingUsers = connections.Where(c => ConnectionToUser.ContainsKey(c.Id))
                                               .Select(c => ConnectionToUser[c.Id])
                                               .ToList();

                await BroadcastToServer(serverId, new WebSocketMessage
                {
                    Type = "users-updated",
                    Data = JsonSerializer.Serialize(remainingUsers)
                });
            }


            ConnectionToUser.TryRemove(connection.Id, out _);
            UserConnections.TryRemove(username, out _);
            UserToServer.TryRemove(username, out _);
        }

        private async Task HandleOffer(WebSocketConnection connection, string targetUser, string offer)
        {
            var senderUser = ConnectionToUser.GetValueOrDefault(connection.Id);
            if (senderUser != null && UserConnections.TryGetValue(targetUser, out var targetConnection))
            {
                await SendToConnection(targetConnection, new WebSocketMessage
                {
                    Type = "offer",
                    Username = senderUser,
                    Data = offer
                });
            }
        }

        private async Task HandleAnswer(WebSocketConnection connection, string targetUser, string answer)
        {
            var senderUser = ConnectionToUser.GetValueOrDefault(connection.Id);
            if (senderUser != null && UserConnections.TryGetValue(targetUser, out var targetConnection))
            {
                await SendToConnection(targetConnection, new WebSocketMessage
                {
                    Type = "answer",
                    Username = senderUser,
                    Data = answer
                });
            }
        }

        private async Task HandleIceCandidate(WebSocketConnection connection, string targetUser, string candidate)
        {
            var senderUser = ConnectionToUser.GetValueOrDefault(connection.Id);
            if (senderUser != null && UserConnections.TryGetValue(targetUser, out var targetConnection))
            {
                await SendToConnection(targetConnection, new WebSocketMessage
                {
                    Type = "ice-candidate",
                    Username = senderUser,
                    Data = candidate
                });
            }
        }

        private async Task HandleServerOffer(WebSocketConnection connection, string offer)
        {
            var senderUser = ConnectionToUser.GetValueOrDefault(connection.Id);
            if (senderUser == null) return;

            Console.WriteLine($"got server offer from {senderUser}");

            if (UserToServer.TryGetValue(senderUser, out var serverId))
            {
                await BroadcastToServer(serverId, new WebSocketMessage
                {
                    Type = "peer-offer",
                    Username = senderUser,
                    Data = offer
                }, connection.Id);
            }
        }

        private async Task HandleServerAnswer(WebSocketConnection connection, string answer)
        {
            var senderUser = ConnectionToUser.GetValueOrDefault(connection.Id);
            if (senderUser == null) return;

            Console.WriteLine($"got server answer from {senderUser}");
            

            if (UserToServer.TryGetValue(senderUser, out var serverId))
            {
                await BroadcastToServer(serverId, new WebSocketMessage
                {
                    Type = "peer-answer",
                    Username = senderUser,
                    Data = answer
                }, connection.Id);
            }
        }

        private async Task HandleServerIceCandidate(WebSocketConnection connection, string candidate)
        {
            var senderUser = ConnectionToUser.GetValueOrDefault(connection.Id);
            if (senderUser == null) return;

            Console.WriteLine($"got ice candidate from {senderUser}");
            

            if (UserToServer.TryGetValue(senderUser, out var serverId))
            {
                await BroadcastToServer(serverId, new WebSocketMessage
                {
                    Type = "peer-ice-candidate",
                    Username = senderUser,
                    Data = candidate
                }, connection.Id);
            }
        }

        private async Task HandleAudioData(WebSocketConnection connection, string audioData)
        {
            var senderUser = ConnectionToUser.GetValueOrDefault(connection.Id);
            if (senderUser == null || !UserToServer.TryGetValue(senderUser, out var serverId))
                return;

            await BroadcastToServer(serverId, new WebSocketMessage
            {
                Type = "audio-data",
                Username = senderUser,
                Data = audioData
            }, connection.Id);
        }

        private async Task HandlePeerOffer(WebSocketConnection connection, string targetUser, string offer)
        {
            var senderUser = ConnectionToUser.GetValueOrDefault(connection.Id);
            Console.WriteLine($"[WS] HandlePeerOffer: {senderUser} -> {targetUser}");
            
            if (senderUser != null && UserConnections.TryGetValue(targetUser, out var targetConnection))
            {
                await SendToConnection(targetConnection, new WebSocketMessage
                {
                    Type = "peer-offer",
                    Username = senderUser,
                    Data = offer
                });
            }
            else
            {
                Console.WriteLine($"[WS] Failed to route offer. Sender: {senderUser}, TargetConnFound: {UserConnections.ContainsKey(targetUser)}");
            }
        }

        private async Task HandlePeerAnswer(WebSocketConnection connection, string targetUser, string answer)
        {
            var senderUser = ConnectionToUser.GetValueOrDefault(connection.Id);
            if (senderUser != null && UserConnections.TryGetValue(targetUser, out var targetConnection))
            {
                await SendToConnection(targetConnection, new WebSocketMessage
                {
                    Type = "peer-answer",
                    Username = senderUser,
                    Data = answer
                });
            }
        }

        private async Task HandlePeerIceCandidate(WebSocketConnection connection, string targetUser, string candidate)
        {
            var senderUser = ConnectionToUser.GetValueOrDefault(connection.Id);
            if (senderUser != null && UserConnections.TryGetValue(targetUser, out var targetConnection))
            {
                await SendToConnection(targetConnection, new WebSocketMessage
                {
                    Type = "peer-ice-candidate",
                    Username = senderUser,
                    Data = candidate
                });
            }
        }

        private async Task HandleDisconnection(WebSocketConnection connection)
        {
            if (ConnectionToUser.TryRemove(connection.Id, out var username))
            {
                if (UserToServer.TryRemove(username, out var serverId))
                {
                    await HandleLeaveVoice(connection, serverId, username);
                }
                UserConnections.TryRemove(username, out _);
            }

            if (connection.WebSocket.State == WebSocketState.Open)
            {
                await connection.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
            }
        }

        private async Task BroadcastToServer(string serverId, WebSocketMessage message, string? excludeConnectionId = null)
        {
            if (VoiceServerConnections.TryGetValue(serverId, out var connections))
            {
                var messageJson = JsonSerializer.Serialize(message);
                var messageBytes = Encoding.UTF8.GetBytes(messageJson);

                var tasks = connections.Where(c => c.Id != excludeConnectionId && c.WebSocket.State == WebSocketState.Open)
                                     .Select(c => c.WebSocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None));

                await Task.WhenAll(tasks);
            }
        }

        private async Task SendToConnection(WebSocketConnection connection, WebSocketMessage message)
        {
            if (connection.WebSocket.State == WebSocketState.Open)
            {
                var messageJson = JsonSerializer.Serialize(message);
                var messageBytes = Encoding.UTF8.GetBytes(messageJson);
                await connection.WebSocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
    }

    public class WebSocketConnection
    {
        public string Id { get; }
        public WebSocket WebSocket { get; }

        public WebSocketConnection(string id, WebSocket webSocket)
        {
            Id = id;
            WebSocket = webSocket;
        }
    }

    public class WebSocketMessage
    {
        public string Type { get; set; } = string.Empty;
        public string? Username { get; set; }
        public string? ServerId { get; set; }
        public string? TargetUser { get; set; }
        public string? Data { get; set; }
    }
}