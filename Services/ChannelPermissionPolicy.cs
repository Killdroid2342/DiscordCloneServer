using System.Text.Json;
using DiscordCloneServer.Models;

namespace DiscordCloneServer.Services
{
    public static class ChannelPermissionPolicy
    {
        public static string NormalizeRoleName(string? value)
        {
            return (value?.Trim().ToLowerInvariant() ?? string.Empty)
                .Replace(' ', '-');
        }

        public static string[] DeserializeRoleNames(string? rolesJson)
        {
            if (string.IsNullOrWhiteSpace(rolesJson))
            {
                return Array.Empty<string>();
            }

            try
            {
                return (JsonSerializer.Deserialize<string[]>(rolesJson) ?? Array.Empty<string>())
                    .Select(NormalizeRoleName)
                    .Where(role => role.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(role => role)
                    .ToArray();
            }
            catch (JsonException)
            {
                return Array.Empty<string>();
            }
        }

        public static bool CanViewChannel(
            Channel channel,
            CreateServer? server,
            ServerMember? member,
            ServerRole? role,
            string username)
        {
            if (server?.ServerOwner == username)
            {
                return true;
            }

            if (member == null)
            {
                return false;
            }

            var roleName = NormalizeRoleName(member.Role);
            if (HasChannelManagementBypass(roleName, role))
            {
                return true;
            }

            if (!channel.ViewAccessRestricted)
            {
                return true;
            }

            return DeserializeRoleNames(channel.ViewAllowedRolesJson)
                .Contains(roleName, StringComparer.OrdinalIgnoreCase);
        }

        public static bool CanSendMessages(
            Channel channel,
            CreateServer? server,
            ServerMember? member,
            ServerRole? role,
            string username)
        {
            if (channel.Type != "text" ||
                !CanViewChannel(channel, server, member, role, username))
            {
                return false;
            }

            if (server?.ServerOwner == username)
            {
                return true;
            }

            if (member == null)
            {
                return false;
            }

            var roleName = NormalizeRoleName(member.Role);
            if (HasChannelManagementBypass(roleName, role))
            {
                return true;
            }

            if (role?.CanSendMessages == false)
            {
                return false;
            }

            if (!channel.MessageSendRestricted)
            {
                return true;
            }

            return DeserializeRoleNames(channel.MessageSendAllowedRolesJson)
                .Contains(roleName, StringComparer.OrdinalIgnoreCase);
        }

        public static bool CanJoinVoice(
            Channel? channel,
            CreateServer? server,
            ServerMember? member,
            ServerRole? role,
            string username)
        {
            if (server?.ServerOwner == username)
            {
                return true;
            }

            if (member == null)
            {
                return false;
            }

            var roleName = NormalizeRoleName(member.Role);
            if (HasChannelManagementBypass(roleName, role))
            {
                return true;
            }

            if (role?.CanJoinVoice == false)
            {
                return false;
            }

            if (channel == null)
            {
                return true;
            }

            if (!CanViewChannel(channel, server, member, role, username))
            {
                return false;
            }

            if (!channel.VoiceAccessRestricted)
            {
                return true;
            }

            return DeserializeRoleNames(channel.VoiceAllowedRolesJson)
                .Contains(roleName, StringComparer.OrdinalIgnoreCase);
        }

        public static bool CanSpeakOnStage(
            Channel channel,
            CreateServer? server,
            ServerMember? member,
            ServerRole? role,
            string username)
        {
            if (channel.Type != "stage")
            {
                return false;
            }

            if (!CanJoinVoice(channel, server, member, role, username))
            {
                return false;
            }

            if (server?.ServerOwner == username)
            {
                return true;
            }

            var roleName = NormalizeRoleName(member?.Role);
            if (HasChannelManagementBypass(roleName, role) ||
                !channel.StageSpeakerRestricted)
            {
                return true;
            }

            return DeserializeRoleNames(channel.StageSpeakerRolesJson)
                .Contains(roleName, StringComparer.OrdinalIgnoreCase);
        }

        private static bool HasChannelManagementBypass(string roleName, ServerRole? role)
        {
            return roleName is "owner" or "admin" or "moderator" ||
                   role?.CanManageChannels == true ||
                   role?.CanManageServer == true;
        }
    }
}
