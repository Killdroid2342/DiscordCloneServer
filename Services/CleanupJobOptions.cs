namespace DiscordCloneServer.Services
{
    public sealed class CleanupJobOptions
    {
        public const string SectionName = "CleanupJobs";

        public bool Enabled { get; set; } = true;
        public int IntervalMinutes { get; set; } = 30;
        public int StartupDelaySeconds { get; set; } = 15;
        public int BatchSize { get; set; } = 500;
        public int RevokedSessionRetentionDays { get; set; } = 7;
        public int ConsumedVerificationRetentionDays { get; set; } = 1;
        public int InactiveInviteRetentionDays { get; set; } = 30;
        public int OrphanUploadRetentionHours { get; set; } = 24;
        public bool CleanupOrphanUploads { get; set; } = true;
    }
}
