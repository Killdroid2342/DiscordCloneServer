using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using DiscordCloneServer.Controllers;
using DiscordCloneServer.Data;
using DiscordCloneServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace DiscordCloneServer.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private static readonly ConcurrentDictionary<string, HashSet<string>> ServerConnections = new();
        private static readonly ConcurrentDictionary<string, string> UserConnections = new();
        private readonly ApiContext _context;

        public ChatHub(ApiContext context)
        {
            _context = context;
        }

        public async Task JoinServer(string serverId, string username)
        {
            username = Context.User?.GetUsername() ?? throw new HubException("Missing user identity.");
            if (!await IsServerMember(serverId, username))
            {
                throw new HubException("You are not a member of this server.");
            }
        
            UserConnections[Context.ConnectionId] = username;
            
        
            var connections = ServerConnections.GetOrAdd(serverId, _ => new HashSet<string>());
            bool isNewUser;
            lock (connections)
            {
                isNewUser = connections.Add(username);
            }
            
    
            await Groups.AddToGroupAsync(Context.ConnectionId, serverId);
            await Clients.Caller.SendAsync(
                "VoiceUsersUpdated",
                serverId,
                VoiceWebSocketController.GetActiveUsersForServer(serverId)
            );
            
    
            if (isNewUser)
            {
        
                await Clients.GroupExcept(serverId, Context.ConnectionId).SendAsync("UserJoined", username);
            }
            
    
            await Clients.Group(serverId).SendAsync("UsersInServer", connections.ToList());
        }

        public async Task LeaveServer(string serverId, string username)
        {
            username = Context.User?.GetUsername() ?? throw new HubException("Missing user identity.");
            if (!await IsServerMember(serverId, username))
            {
                throw new HubException("You are not a member of this server.");
            }
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, serverId);
            
            if (ServerConnections.TryGetValue(serverId, out var connections))
            {
                lock (connections)
                {
                    connections.Remove(username);
                }
                
        
                await Clients.Group(serverId).SendAsync("UserLeft", username);
                
        
                await Clients.Group(serverId).SendAsync("UsersInServer", connections.ToList());
            }
        }

        public async Task SendMessage(string serverId, string username, string message, string date)
        {
            username = Context.User?.GetUsername() ?? throw new HubException("Missing user identity.");
            if (!await IsServerMember(serverId, username))
            {
                throw new HubException("You are not a member of this server.");
            }
    
            await Clients.Group(serverId).SendAsync("ReceiveMessage", username, message, date);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (UserConnections.TryRemove(Context.ConnectionId, out var username))
            {
        
                foreach (var serverConnections in ServerConnections.Values)
                {
                    lock (serverConnections)
                    {
                        serverConnections.Remove(username);
                    }
                }
                
        
                foreach (var serverId in ServerConnections.Keys)
                {
                    await Clients.Group(serverId).SendAsync("UserLeft", username);
                }
            }
            
            await base.OnDisconnectedAsync(exception);
        }

        private async Task<bool> IsServerMember(string serverId, string username)
        {
            return await _context.ServerMembers.AnyAsync(member =>
                member.ServerId == serverId && member.Username == username);
        }
    }
}
