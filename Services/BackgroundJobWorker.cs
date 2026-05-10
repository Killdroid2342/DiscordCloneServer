using Microsoft.Extensions.Options;

namespace DiscordCloneServer.Services
{
    public sealed class BackgroundJobWorker : BackgroundService
    {
        private readonly IBackgroundJobQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOptionsMonitor<BackgroundJobOptions> _options;
        private readonly ILogger<BackgroundJobWorker> _logger;

        public BackgroundJobWorker(
            IBackgroundJobQueue queue,
            IServiceScopeFactory scopeFactory,
            IOptionsMonitor<BackgroundJobOptions> options,
            ILogger<BackgroundJobWorker> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _options = options;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.CurrentValue.Enabled)
            {
                _logger.LogInformation("Background job worker is disabled");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                BackgroundJob workItem;
                try
                {
                    workItem = await _queue.DequeueAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    await workItem(scope.ServiceProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background job failed");
                }
            }
        }
    }
}
