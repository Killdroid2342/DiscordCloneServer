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

    
}
