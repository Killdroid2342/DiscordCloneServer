using Microsoft.AspNetCore.Http;
using System.Threading;

namespace DiscordCloneServer.Services
{
    public sealed class MonitoringMetrics
    {
        private long _totalRequests;
        private long _clientErrorRequests;
        private long _serverErrorRequests;
        private long _totalRequestDurationMs;

        public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;

        public MonitoringSnapshot Snapshot()
        {
            var totalRequests = Interlocked.Read(ref _totalRequests);
            var totalRequestDurationMs = Interlocked.Read(ref _totalRequestDurationMs);

            return new MonitoringSnapshot(
                StartedAtUtc,
                DateTimeOffset.UtcNow - StartedAtUtc,
                totalRequests,
                Interlocked.Read(ref _clientErrorRequests),
                Interlocked.Read(ref _serverErrorRequests),
                totalRequestDurationMs,
                totalRequests == 0 ? 0 : Math.Round(totalRequestDurationMs / (double)totalRequests, 2));
        }

        public void RecordRequest(int statusCode, long elapsedMilliseconds)
        {
            Interlocked.Increment(ref _totalRequests);
            Interlocked.Add(ref _totalRequestDurationMs, Math.Max(0, elapsedMilliseconds));

            if (statusCode >= StatusCodes.Status500InternalServerError)
            {
                Interlocked.Increment(ref _serverErrorRequests);
            }
            else if (statusCode >= StatusCodes.Status400BadRequest)
            {
                Interlocked.Increment(ref _clientErrorRequests);
            }
        }
    }

    public sealed record MonitoringSnapshot(
        DateTimeOffset StartedAtUtc,
        TimeSpan Uptime,
        long TotalRequests,
        long ClientErrorRequests,
        long ServerErrorRequests,
        long TotalRequestDurationMs,
        double AverageRequestDurationMs);
}
