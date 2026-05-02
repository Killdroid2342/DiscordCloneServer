namespace DiscordCloneServer.Services
{
    public sealed record CleanupJobResult(
        int ExpiredSessionsDeleted,
        int ContactVerificationsDeleted,
        int ExpiredAccountSecretsCleared,
        int ExpiredMemberModerationStatesCleared,
        int InactiveInvitesDeleted,
        int StaleServerInviteLinksCleared,
        int OrphanUploadsDeleted)
    {
        public int TotalChanges =>
            ExpiredSessionsDeleted +
            ContactVerificationsDeleted +
            ExpiredAccountSecretsCleared +
            ExpiredMemberModerationStatesCleared +
            InactiveInvitesDeleted +
            StaleServerInviteLinksCleared +
            OrphanUploadsDeleted;
    }
}
