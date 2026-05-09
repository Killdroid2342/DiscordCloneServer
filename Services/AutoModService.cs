using System.Text.Json;
using System.Text.RegularExpressions;
using DiscordCloneServer.Data;
using DiscordCloneServer.Models;
using Microsoft.EntityFrameworkCore;

namespace DiscordCloneServer.Services
{
    public sealed record AutoModCheckRequest(
        string ServerId,
        string ScopeType,
        string ScopeId,
        string SenderUsername,
        string MessageText,
        string? AttachmentUrl);

    public sealed record AutoModCheckResult(
        bool Allowed,
        string ReasonCode,
        string Message,
        string? RuleId = null,
        string? RuleName = null)
    {
        public static AutoModCheckResult Allow { get; } =
            new(true, "allowed", string.Empty);
    }

    public interface IAutoModService
    {
        Task<AutoModCheckResult> CheckAsync(
            AutoModCheckRequest request,
            CancellationToken cancellationToken = default);
    }

    
}
