using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace DiscordCloneServer.Services
{
    public delegate ValueTask BackgroundJob(IServiceProvider services, CancellationToken cancellationToken);

    public interface IBackgroundJobQueue
    {
        bool TryQueue(BackgroundJob workItem);
        ValueTask<BackgroundJob> DequeueAsync(CancellationToken cancellationToken);
    }

    public sealed class BackgroundJobQueue : IBackgroundJobQueue
    {
        private readonly Channel<BackgroundJob> _queue;
        private readonly bool _enabled;

        public BackgroundJobQueue(IOptions<BackgroundJobOptions> options)
        {
            _enabled = options.Value.Enabled;
            var capacity = Math.Clamp(options.Value.QueueCapacity, 1, 100_000);
            _queue = Channel.CreateBounded<BackgroundJob>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            });
        }

        public bool TryQueue(BackgroundJob workItem)
        {
            ArgumentNullException.ThrowIfNull(workItem);
            if (!_enabled)
            {
                return false;
            }

            return _queue.Writer.TryWrite(workItem);
        }

        public async ValueTask<BackgroundJob> DequeueAsync(CancellationToken cancellationToken)
        {
            return await _queue.Reader.ReadAsync(cancellationToken);
        }
    }
}
