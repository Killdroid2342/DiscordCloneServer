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

        
    }
}
