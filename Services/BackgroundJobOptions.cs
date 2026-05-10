namespace DiscordCloneServer.Services
{
    public sealed class BackgroundJobOptions
    {
        public const string SectionName = "BackgroundJobs";

        public bool Enabled { get; set; } = true;
        public int QueueCapacity { get; set; } = 1000;
    }
}
