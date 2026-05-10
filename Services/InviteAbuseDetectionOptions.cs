namespace DiscordCloneServer.Services
{
    public sealed class InviteAbuseDetectionOptions
    {
        public const string SectionName = "InviteAbuseDetection";

        public bool Enabled { get; set; } = true;
        public int WindowMinutes { get; set; } = 10;
        public int MaxUsesPerInviteWindow { get; set; } = 10;
        public int MaxUsesPerIpWindow { get; set; } = 5;
        public bool AutoRevokeDetectedInvites { get; set; } = true;

        public void Normalize()
        {
            WindowMinutes = Math.Clamp(WindowMinutes, 1, 24 * 60);
            MaxUsesPerInviteWindow = Math.Max(0, MaxUsesPerInviteWindow);
            MaxUsesPerIpWindow = Math.Max(0, MaxUsesPerIpWindow);
        }
    }
}
