using Microsoft.Extensions.Options;

namespace DiscordCloneServer.Services
{
    public sealed class BackgroundCleanupService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOptionsMonitor<CleanupJobOptions> _options;
        private readonly ILogger<BackgroundCleanupService> _logger;

        public BackgroundCleanupService(
            IServiceScopeFactory scopeFactory,
            IOptionsMonitor<CleanupJobOptions> options,
            ILogger<BackgroundCleanupService> logger)
        {
            _scopeFactory = scopeFactory;
            _options = options;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.CurrentValue.Enabled)
            {
                _logger.LogInformation("Background cleanup jobs are disabled");
                return;
            }

            var startupDelay = TimeSpan.FromSeconds(Math.Max(0, _options.CurrentValue.StartupDelaySeconds));
            if (startupDelay > TimeSpan.Zero)
            {
                await Task.Delay(startupDelay, stoppingToken);
            }

            await RunCleanupAsync(stoppingToken);

            using var timer = new PeriodicTimer(GetInterval());
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (_options.CurrentValue.Enabled)
                {
                    await RunCleanupAsync(stoppingToken);
                }
            }
        }

        private async Task RunCleanupAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<CleanupJobRunner>();
                var result = await runner.RunOnceAsync(stoppingToken);

                if (result.TotalChanges > 0)
                {
                    _logger.LogInformation(
                        "Background cleanup completed with {TotalChanges} changes: {ExpiredSessionsDeleted} sessions, {ContactVerificationsDeleted} contact verifications, {ExpiredAccountSecretsCleared} account secrets, {ExpiredMemberModerationStatesCleared} moderation states, {InactiveInvitesDeleted} invites, {StaleServerInviteLinksCleared} invite links, {OrphanUploadsDeleted} uploads",
                        result.TotalChanges,
                        result.ExpiredSessionsDeleted,
                        result.ContactVerificationsDeleted,
                        result.ExpiredAccountSecretsCleared,
                        result.ExpiredMemberModerationStatesCleared,
                        result.InactiveInvitesDeleted,
                        result.StaleServerInviteLinksCleared,
                        result.OrphanUploadsDeleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background cleanup failed");
            }
        }

        private TimeSpan GetInterval()
        {
            return TimeSpan.FromMinutes(Math.Max(1, _options.CurrentValue.IntervalMinutes));
        }
    }
}
