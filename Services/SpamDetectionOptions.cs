namespace DiscordCloneServer.Services
{
    public sealed class SpamDetectionOptions
    {
        public const string SectionName = "SpamDetection";

        public bool Enabled { get; set; } = true;
        public int BurstMessageLimit { get; set; } = 6;
        public int BurstWindowSeconds { get; set; } = 10;
        public int DuplicateMessageLimit { get; set; } = 3;
        public int DuplicateWindowSeconds { get; set; } = 90;
        public int LinkMessageLimit { get; set; } = 4;
        public int LinkWindowSeconds { get; set; } = 120;
        public int MaxMentionsPerMessage { get; set; } = 12;
        public int MaxRepeatedCharacterRun { get; set; } = 24;
        public int MaxRepeatedWordRun { get; set; } = 12;
    }
}
