using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using DiscordCloneServer.Hubs;
using DiscordCloneServer.Data;
using DiscordCloneServer.Models;
using DiscordCloneServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace DiscordCloneServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class VoiceWebSocketController : ControllerBase
    {
        private static readonly ConcurrentDictionary<string, HashSet<WebSocketConnection>> VoiceServerConnections = new();
        private static readonly ConcurrentDictionary<string, HashSet<WebSocketConnection>> VoiceServerWatchers = new();
        private static readonly ConcurrentDictionary<string, WebSocketConnection> UserConnections = new();
        private static readonly ConcurrentDictionary<string, string> ConnectionToUser = new();
        private static readonly ConcurrentDictionary<string, string> ConnectionToWatchedServer = new();
        private static readonly ConcurrentDictionary<string, string> UserToServer = new();
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ApiContext _context;

        public VoiceWebSocketController(IHubContext<ChatHub> hubContext, ApiContext context)
        {
            _hubContext = hubContext;
            _context = context;
        }

        [HttpGet("/voice-ws")]
        public async Task HandleWebSocket()
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
            {
                HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
                var connectionId = Guid.NewGuid().ToString();
                var connection = new WebSocketConnection(connectionId, webSocket, currentUsername);

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
                    case "identify":
                        await HandleIdentify(connection, connection.Username);
                        break;
                    case "join":
                        if (messageObj.ServerId != null && await CanJoinVoice(messageObj.ServerId, connection.Username, messageObj.ChannelId))
                            await HandleJoinVoice(connection, messageObj.ServerId, connection.Username, messageObj.ChannelId);
                        break;
                    case "leave":
                        if (messageObj.ServerId != null)
                            await HandleLeaveVoice(connection, messageObj.ServerId, connection.Username);
                        break;
                    case "watch":
                        if (messageObj.ServerId != null && await CanJoinVoice(messageObj.ServerId, connection.Username))
                            await HandleWatchVoice(connection, messageObj.ServerId);
                        break;
                    case "unwatch":
                        if (messageObj.ServerId != null)
                            HandleUnwatchVoice(connection, messageObj.ServerId);
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
                            await HandlePeerOffer(connection, messageObj);
                        break;
                    case "peer-answer":
                        if (messageObj.TargetUser != null && messageObj.Data != null)
                            await HandlePeerAnswer(connection, messageObj);
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
                    case "call-ended":
                        if (messageObj.TargetUser != null)
                            await HandleCallEnded(connection, messageObj.TargetUser);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"couldnt process voice msg: {ex.Message}");
            }
        }

        private async Task HandleIdentify(WebSocketConnection connection, string username)
        {
            ConnectionToUser[connection.Id] = username;
            UserConnections[username] = connection;
            Console.WriteLine($"[WS] Identified user for DM: {username}");
        }

        private async Task<bool> IsServerMember(string serverId, string username)
        {
            return await _context.ServerMembers.AnyAsync(member =>
                member.ServerId == serverId && member.Username == username);
        }

        private async Task<bool> CanJoinVoice(string serverId, string username, string? channelId = null)
        {
            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == serverId);
            if (server == null)
            {
                return false;
            }

            var channel = string.IsNullOrWhiteSpace(channelId)
                ? null
                : await _context.Channels.FirstOrDefaultAsync(channel =>
                    channel.Id == channelId && channel.ServerId == serverId);
            if (!string.IsNullOrWhiteSpace(channelId) &&
                (channel == null || !IsVoiceLikeChannelType(channel.Type)))
            {
                return false;
            }

            if (server.ServerOwner == username)
            {
                return true;
            }

            var member = await _context.ServerMembers.FirstOrDefaultAsync(m =>
                m.ServerId == serverId && m.Username == username);
            if (member == null)
            {
                return false;
            }

            if (IsMemberTimedOut(member, DateTime.UtcNow))
            {
                return false;
            }

            var roleName = member.Role?.Trim().ToLowerInvariant() ?? "user";
            if (roleName is "owner" or "admin" or "moderator")
            {
                return true;
            }

            var role = await _context.ServerRoles.FirstOrDefaultAsync(r => r.ServerId == serverId && r.Name == roleName);
            if (role?.CanJoinVoice == false)
            {
                return false;
            }

            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.UserName == username && !a.IsDisabled);
            if (!ServerVerificationPolicy.Evaluate(server.VerificationLevel, account, member).Allowed)
            {
                return false;
            }

            if (channel is { VoiceAccessRestricted: true })
            {
                var allowedRoles = DeserializeRoleNames(channel.VoiceAllowedRolesJson);
                return allowedRoles.Contains(roleName, StringComparer.OrdinalIgnoreCase);
            }

            return true;
        }

        private static bool IsMemberTimedOut(ServerMember member, DateTime now)
        {
            return member.TimedOutUntil is { } timedOutUntil && timedOutUntil > now;
        }

        private static bool IsVoiceLikeChannelType(string? value)
        {
            var type = value?.Trim().ToLowerInvariant();
            return type is "voice" or "stage";
        }

        private static string[] DeserializeRoleNames(string? rolesJson)
        {
            if (string.IsNullOrWhiteSpace(rolesJson))
            {
                return Array.Empty<string>();
            }

            try
            {
                return (JsonSerializer.Deserialize<string[]>(rolesJson) ?? Array.Empty<string>())
                    .Select(role => role.Trim().ToLowerInvariant().Replace(' ', '-'))
                    .Where(role => role.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (JsonException)
            {
                return Array.Empty<string>();
            }
        }

        private List<string> GetUsersForServerSnapshot(string serverId)
        {
            if (!VoiceServerConnections.TryGetValue(serverId, out var connections))
            {
                return new List<string>();
            }

            lock (connections)
            {
                return connections
                    .Select(c => ConnectionToUser.TryGetValue(c.Id, out var connectedUser) ? connectedUser : null)
                    .Where(connectedUser => !string.IsNullOrWhiteSpace(connectedUser))
                    .Distinct()
                    .Cast<string>()
                    .ToList();
            }
        }

        public static List<string> GetActiveUsersForServer(string serverId)
        {
            if (!VoiceServerConnections.TryGetValue(serverId, out var connections))
            {
                return new List<string>();
            }

            lock (connections)
            {
                return connections
                    .Select(c => ConnectionToUser.TryGetValue(c.Id, out var connectedUser) ? connectedUser : null)
                    .Where(connectedUser => !string.IsNullOrWhiteSpace(connectedUser))
                    .Distinct()
                    .Cast<string>()
                    .ToList();
            }
        }

        private Task NotifyServerVoiceUsersUpdated(string serverId, List<string> users)
        {
            return _hubContext.Clients.Group(serverId)
                .SendAsync("VoiceUsersUpdated", serverId, users);
        }

        private async Task HandleWatchVoice(WebSocketConnection connection, string serverId)
        {
            if (ConnectionToWatchedServer.TryGetValue(connection.Id, out var previousServerId) &&
                previousServerId != serverId)
            {
                RemoveWatcherFromServer(connection, previousServerId);
            }

            var watchers = VoiceServerWatchers.GetOrAdd(serverId, _ => new HashSet<WebSocketConnection>());
            lock (watchers)
            {
                watchers.Add(connection);
            }
            ConnectionToWatchedServer[connection.Id] = serverId;

            await SendVoiceUsersUpdatedToConnection(connection, serverId, GetUsersForServerSnapshot(serverId));
        }

        private void HandleUnwatchVoice(WebSocketConnection connection, string serverId)
        {
            RemoveWatcherFromServer(connection, serverId);

            if (ConnectionToWatchedServer.TryGetValue(connection.Id, out var watchedServerId) &&
                watchedServerId == serverId)
            {
                ConnectionToWatchedServer.TryRemove(connection.Id, out _);
            }
        }

        private void RemoveWatcherFromServer(WebSocketConnection connection, string serverId)
        {
            if (!VoiceServerWatchers.TryGetValue(serverId, out var watchers))
            {
                return;
            }

            lock (watchers)
            {
                watchers.Remove(connection);
            }
        }

        private void RemoveWatchedServer(WebSocketConnection connection)
        {
            if (ConnectionToWatchedServer.TryRemove(connection.Id, out var watchedServerId))
            {
                RemoveWatcherFromServer(connection, watchedServerId);
            }
        }

        private async Task BroadcastVoiceUsersUpdated(string serverId, List<string> users)
        {
            var message = new WebSocketMessage
            {
                Type = "users-updated",
                ServerId = serverId,
                Data = JsonSerializer.Serialize(users)
            };

            var targetConnections = new List<WebSocketConnection>();

            if (VoiceServerConnections.TryGetValue(serverId, out var voiceConnections))
            {
                lock (voiceConnections)
                {
                    targetConnections.AddRange(voiceConnections);
                }
            }

            if (VoiceServerWatchers.TryGetValue(serverId, out var watchers))
            {
                lock (watchers)
                {
                    targetConnections.AddRange(watchers);
                }
            }

            var tasks = targetConnections
                .Where(c => c.WebSocket.State == WebSocketState.Open)
                .GroupBy(c => c.Id)
                .Select(group => group.First())
                .Select(c => SendToConnection(c, message));

            await Task.WhenAll(tasks);
        }

        private Task SendVoiceUsersUpdatedToConnection(WebSocketConnection connection, string serverId, List<string> users)
        {
            return SendToConnection(connection, new WebSocketMessage
            {
                Type = "users-updated",
                ServerId = serverId,
                Data = JsonSerializer.Serialize(users)
            });
        }

        private async Task HandleJoinVoice(WebSocketConnection connection, string serverId, string username, string? channelId = null)
        {
            if (UserToServer.TryGetValue(username, out var previousServerId) &&
                !string.IsNullOrWhiteSpace(previousServerId) &&
                previousServerId != serverId)
            {
                await HandleLeaveVoice(connection, previousServerId, username, preserveIdentity: true);
            }

            ConnectionToUser[connection.Id] = username;
            UserConnections[username] = connection;
            UserToServer[username] = serverId;

            var connections = VoiceServerConnections.GetOrAdd(serverId, _ => new HashSet<WebSocketConnection>());
            List<string> existingUsers;
            bool alreadyJoined;

            lock (connections)
            {
                existingUsers = connections
                    .Select(c => ConnectionToUser.TryGetValue(c.Id, out var connectedUser) ? connectedUser : null)
                    .Where(connectedUser => !string.IsNullOrWhiteSpace(connectedUser))
                    .Where(existingUser => existingUser != username)
                    .Distinct()
                    .Cast<string>()
                    .ToList();
                alreadyJoined = connections.Contains(connection) ||
                                connections.Any(c => ConnectionToUser.TryGetValue(c.Id, out var existingUser) &&
                                                     existingUser == username);
                connections.Add(connection);
            }

            if (!alreadyJoined)
            {
                await BroadcastToServer(serverId, new WebSocketMessage
                {
                    Type = "user-joined",
                    ServerId = serverId,
                    ChannelId = channelId,
                    Username = username
                }, connection.Id);
            }


            await SendToConnection(connection, new WebSocketMessage
            {
                Type = "existing-users",
                ServerId = serverId,
                ChannelId = channelId,
                Data = JsonSerializer.Serialize(existingUsers)
            });


            var allUsers = GetUsersForServerSnapshot(serverId);
            await BroadcastVoiceUsersUpdated(serverId, allUsers);
            await NotifyServerVoiceUsersUpdated(serverId, allUsers);
        }

        private async Task HandleLeaveVoice(WebSocketConnection connection, string serverId, string username, bool preserveIdentity = false)
        {
            if (VoiceServerConnections.TryGetValue(serverId, out var connections))
            {
                lock (connections)
                {
                    connections.Remove(connection);
                }


                bool isUserStillPresent = false;
                lock (connections)
                {
                    isUserStillPresent = connections.Any(c => ConnectionToUser.TryGetValue(c.Id, out var u) && u == username);
                }

                if (!isUserStillPresent)
                {

                    await BroadcastToServer(serverId, new WebSocketMessage
                    {
                        Type = "user-left",
                        ServerId = serverId,
                        Username = username
                    });


                    if (!preserveIdentity &&
                        UserConnections.TryGetValue(username, out var uc) &&
                        uc.Id == connection.Id)
                    {
                        UserConnections.TryRemove(username, out _);
                        UserToServer.TryRemove(username, out _);
                    }
                }


                if (!preserveIdentity)
                {
                    ConnectionToUser.TryRemove(connection.Id, out _);
                }


                List<string> remainingUsers = GetUsersForServerSnapshot(serverId);

                await BroadcastVoiceUsersUpdated(serverId, remainingUsers);
                await NotifyServerVoiceUsersUpdated(serverId, remainingUsers);
                 

                if (connection.WebSocket.State == WebSocketState.Open)
                {
                    await SendVoiceUsersUpdatedToConnection(connection, serverId, remainingUsers);
                }

            }
            else if (!preserveIdentity)
            {
                ConnectionToUser.TryRemove(connection.Id, out _);
                if (UserConnections.TryGetValue(username, out var uc) && uc.Id == connection.Id)
                {
                    UserConnections.TryRemove(username, out _);
                }
                UserToServer.TryRemove(username, out _);
            }
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

        private async Task HandlePeerOffer(WebSocketConnection connection, WebSocketMessage message)
        {
            var senderUser = ConnectionToUser.GetValueOrDefault(connection.Id);
            var targetUser = message.TargetUser ?? string.Empty;
            Console.WriteLine($"[WS] HandlePeerOffer: {senderUser} -> {targetUser}");
            
            if (senderUser != null && UserConnections.TryGetValue(targetUser, out var targetConnection))
            {
                await SendToConnection(targetConnection, new WebSocketMessage
                {
                    Type = "peer-offer",
                    Username = senderUser,
                    Data = message.Data,
                    IsPrivate = message.IsPrivate,
                    IsVideo = message.IsVideo
                });
            }
            else
            {
                Console.WriteLine($"[WS] Failed to route offer. Sender: {senderUser}, TargetConnFound: {UserConnections.ContainsKey(targetUser)}");
            }
        }

        private async Task HandlePeerAnswer(WebSocketConnection connection, WebSocketMessage message)
        {
            var senderUser = ConnectionToUser.GetValueOrDefault(connection.Id);
            var targetUser = message.TargetUser ?? string.Empty;
            if (senderUser != null && UserConnections.TryGetValue(targetUser, out var targetConnection))
            {
                await SendToConnection(targetConnection, new WebSocketMessage
                {
                    Type = "peer-answer",
                    Username = senderUser,
                    Data = message.Data,
                    IsPrivate = message.IsPrivate,
                    IsVideo = message.IsVideo
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

        private async Task HandleCallEnded(WebSocketConnection connection, string targetUser)
        {
            var senderUser = ConnectionToUser.GetValueOrDefault(connection.Id);
            if (senderUser != null && UserConnections.TryGetValue(targetUser, out var targetConnection))
            {
                await SendToConnection(targetConnection, new WebSocketMessage
                {
                    Type = "call-ended",
                    Username = senderUser
                });
            }
        }

        private async Task HandleDisconnection(WebSocketConnection connection)
        {
            RemoveWatchedServer(connection);

            if (ConnectionToUser.TryGetValue(connection.Id, out var username))
            {

                if (UserToServer.TryGetValue(username, out var serverId))
                {
                    await HandleLeaveVoice(connection, serverId, username);
                }
                else
                {
                    ConnectionToUser.TryRemove(connection.Id, out _);

                    if (UserConnections.TryGetValue(username, out var uc) && uc.Id == connection.Id)
                    {
                        UserConnections.TryRemove(username, out _);
                    }
                }
                


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
                List<WebSocketConnection> targetConnections;

                lock (connections)
                {
                    targetConnections = connections
                        .Where(c => c.Id != excludeConnectionId && c.WebSocket.State == WebSocketState.Open)
                        .ToList();
                }

                var tasks = targetConnections
                    .Select(c => SendToConnection(c, message));

                await Task.WhenAll(tasks);
            }
        }

        private async Task SendToConnection(WebSocketConnection connection, WebSocketMessage message)
        {
            if (connection.WebSocket.State == WebSocketState.Open)
            {
                var messageJson = JsonSerializer.Serialize(message);
                var messageBytes = Encoding.UTF8.GetBytes(messageJson);
                try
                {
                    await connection.WebSocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch (WebSocketException)
                {
                    connection.WebSocket.Abort();
                    await HandleDisconnection(connection);
                }
            }
        }

    }

    public class WebSocketConnection
    {
        public string Id { get; }
        public WebSocket WebSocket { get; }
        public string Username { get; }

        public WebSocketConnection(string id, WebSocket webSocket, string username)
        {
            Id = id;
            WebSocket = webSocket;
            Username = username;
        }
    }

    public class WebSocketMessage
    {
        public string Type { get; set; } = string.Empty;
        public string? Username { get; set; }
        public string? ServerId { get; set; }
        public string? ChannelId { get; set; }
        public string? TargetUser { get; set; }
        public string? Data { get; set; }
        public bool IsPrivate { get; set; }
        public bool IsVideo { get; set; }
    }
}
