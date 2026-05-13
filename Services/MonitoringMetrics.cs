using Microsoft.AspNetCore.Http;
using System.Threading;

namespace DiscordCloneServer.Services
{
    public sealed class MonitoringMetrics
    {
        private const int MaxProfiledEndpoints = 200;

        private long _totalRequests;
        private long _clientErrorRequests;
        private long _serverErrorRequests;
        private long _totalRequestDurationMs;
        private readonly object _endpointProfilesLock = new();
        private readonly Dictionary<string, EndpointPerformanceAccumulator> _endpointProfiles = new(StringComparer.OrdinalIgnoreCase);

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

        public void RecordRequest(int statusCode, long elapsedMilliseconds, string? method = null, string? path = null)
        {
            var safeElapsedMilliseconds = Math.Max(0, elapsedMilliseconds);

            Interlocked.Increment(ref _totalRequests);
            Interlocked.Add(ref _totalRequestDurationMs, safeElapsedMilliseconds);

            if (statusCode >= StatusCodes.Status500InternalServerError)
            {
                Interlocked.Increment(ref _serverErrorRequests);
            }
            else if (statusCode >= StatusCodes.Status400BadRequest)
            {
                Interlocked.Increment(ref _clientErrorRequests);
            }

            RecordEndpointProfile(statusCode, safeElapsedMilliseconds, method, path);
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

    public sealed record PerformanceProfileSnapshot(
        DateTimeOffset StartedAtUtc,
        DateTimeOffset GeneratedAtUtc,
        long TotalProfiledRequests,
        EndpointPerformanceSnapshot[] SlowestEndpoints,
        EndpointPerformanceSnapshot[] BusiestEndpoints);

    public sealed record EndpointPerformanceSnapshot(
        string Method,
        string Path,
        long TotalRequests,
        long ClientErrorRequests,
        long ServerErrorRequests,
        long TotalDurationMs,
        double AverageDurationMs,
        long MinDurationMs,
        long MaxDurationMs,
        long LastDurationMs,
        DateTimeOffset LastSeenUtc);

    internal sealed class EndpointPerformanceAccumulator
    {
        public EndpointPerformanceAccumulator(string method, string path)
        {
            Method = method;
            Path = path;
        }

        public string Method { get; }
        public string Path { get; }
        public long TotalRequests { get; private set; }
        public long ClientErrorRequests { get; private set; }
        public long ServerErrorRequests { get; private set; }
        public long TotalDurationMs { get; private set; }
        public long MinDurationMs { get; private set; } = long.MaxValue;
        public long MaxDurationMs { get; private set; }
        public long LastDurationMs { get; private set; }
        public DateTimeOffset LastSeenUtc { get; private set; } = DateTimeOffset.UtcNow;

        public void Record(int statusCode, long elapsedMilliseconds)
        {
            TotalRequests++;
            TotalDurationMs += elapsedMilliseconds;
            MinDurationMs = Math.Min(MinDurationMs, elapsedMilliseconds);
            MaxDurationMs = Math.Max(MaxDurationMs, elapsedMilliseconds);
            LastDurationMs = elapsedMilliseconds;
            LastSeenUtc = DateTimeOffset.UtcNow;

            if (statusCode >= StatusCodes.Status500InternalServerError)
            {
                ServerErrorRequests++;
            }
            else if (statusCode >= StatusCodes.Status400BadRequest)
            {
                ClientErrorRequests++;
            }
        }

       
    }
}
