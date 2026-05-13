using DiscordCloneServer.Data;
using DiscordCloneServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace DiscordCloneServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Monitoring)]
    public class HealthController : ControllerBase
    {
        private readonly ApiContext _context;
        private readonly MonitoringMetrics _metrics;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<HealthController> _logger;

        public HealthController(
            ApiContext context,
            MonitoringMetrics metrics,
            IWebHostEnvironment environment,
            ILogger<HealthController> logger)
        {
            _context = context;
            _metrics = metrics;
            _environment = environment;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            var database = await CheckDatabaseAsync(cancellationToken);
            var payload = BuildHealthPayload(database);

            return database.CanConnect
                ? Ok(payload)
                : StatusCode(StatusCodes.Status503ServiceUnavailable, payload);
        }

        [HttpGet("live")]
        public IActionResult Live()
        {
            var snapshot = _metrics.Snapshot();

            return Ok(new
            {
                status = "ok",
                service = "DiscordCloneServer",
                timestampUtc = DateTimeOffset.UtcNow,
                uptimeSeconds = Math.Round(snapshot.Uptime.TotalSeconds, 2)
            });
        }

        [HttpGet("ready")]
        public async Task<IActionResult> Ready(CancellationToken cancellationToken)
        {
            var database = await CheckDatabaseAsync(cancellationToken);

            var payload = new
            {
                status = database.CanConnect ? "ok" : "degraded",
                timestampUtc = DateTimeOffset.UtcNow,
                database = new
                {
                    status = database.Status,
                    latencyMs = database.LatencyMs
                }
            };

            return database.CanConnect
                ? Ok(payload)
                : StatusCode(StatusCodes.Status503ServiceUnavailable, payload);
        }

        [HttpGet("metrics")]
        [Produces("text/plain")]
        public async Task<IActionResult> Metrics(CancellationToken cancellationToken)
        {
            var database = await CheckDatabaseAsync(cancellationToken);
            var snapshot = _metrics.Snapshot();
            var content = new StringBuilder();

            AppendMetric(content, "mydiscord_uptime_seconds", "Total seconds since the API process started.", "gauge", snapshot.Uptime.TotalSeconds);
            AppendMetric(content, "mydiscord_http_requests_total", "Total HTTP requests observed by the API.", "counter", snapshot.TotalRequests);
            AppendMetric(content, "mydiscord_http_client_errors_total", "Total HTTP 4xx responses observed by the API.", "counter", snapshot.ClientErrorRequests);
            AppendMetric(content, "mydiscord_http_server_errors_total", "Total HTTP 5xx responses observed by the API.", "counter", snapshot.ServerErrorRequests);
            AppendMetric(content, "mydiscord_http_request_duration_ms_total", "Total HTTP request duration observed by the API in milliseconds.", "counter", snapshot.TotalRequestDurationMs);
            AppendMetric(content, "mydiscord_http_request_duration_ms_average", "Average HTTP request duration observed by the API in milliseconds.", "gauge", snapshot.AverageRequestDurationMs);
            AppendMetric(content, "mydiscord_database_ready", "Database readiness state, where 1 means ready and 0 means unavailable.", "gauge", database.CanConnect ? 1 : 0);
            AppendMetric(content, "mydiscord_database_latency_ms", "Latest database readiness check duration in milliseconds.", "gauge", database.LatencyMs);
            AppendMetric(content, "mydiscord_process_working_set_bytes", "Current process working set in bytes.", "gauge", Environment.WorkingSet);
            AppendMetric(content, "mydiscord_gc_heap_bytes", "Current managed heap size in bytes.", "gauge", GC.GetTotalMemory(false));

            return Content(content.ToString(), "text/plain; version=0.0.4; charset=utf-8");
        }

        [HttpGet("profile")]
        public IActionResult Profile([FromQuery] int top = 20)
        {
            var snapshot = _metrics.ProfileSnapshot(top);

            return Ok(new
            {
                status = "ok",
                service = "DiscordCloneServer",
                timestampUtc = DateTimeOffset.UtcNow,
                startedAtUtc = snapshot.StartedAtUtc,
                generatedAtUtc = snapshot.GeneratedAtUtc,
                totalProfiledRequests = snapshot.TotalProfiledRequests,
                slowestEndpoints = snapshot.SlowestEndpoints.Select(ToEndpointPayload),
                busiestEndpoints = snapshot.BusiestEndpoints.Select(ToEndpointPayload)
            });
        }

        private object BuildHealthPayload(DatabaseHealth database)
        {
            var snapshot = _metrics.Snapshot();

            return new
            {
                status = database.CanConnect ? "ok" : "degraded",
                service = "DiscordCloneServer",
                environment = _environment.EnvironmentName,
                version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
                timestampUtc = DateTimeOffset.UtcNow,
                startedAtUtc = snapshot.StartedAtUtc,
                uptimeSeconds = Math.Round(snapshot.Uptime.TotalSeconds, 2),
                database = new
                {
                    status = database.Status,
                    latencyMs = database.LatencyMs
                },
                requests = new
                {
                    total = snapshot.TotalRequests,
                    clientErrors = snapshot.ClientErrorRequests,
                    serverErrors = snapshot.ServerErrorRequests,
                    averageLatencyMs = snapshot.AverageRequestDurationMs
                },
                process = new
                {
                    workingSetBytes = Environment.WorkingSet,
                    gcHeapBytes = GC.GetTotalMemory(false)
                }
            };
        }

        private async Task<DatabaseHealth> CheckDatabaseAsync(CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var canConnect = await _context.Database.CanConnectAsync(cancellationToken);
                stopwatch.Stop();

                return new DatabaseHealth(
                    canConnect,
                    canConnect ? "ok" : "unavailable",
                    stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogWarning(ex, "Database health check failed");

                return new DatabaseHealth(false, "error", stopwatch.ElapsedMilliseconds);
            }
        }

        private static void AppendMetric(StringBuilder builder, string name, string help, string type, double value)
        {
            builder.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
            builder.Append("# TYPE ").Append(name).Append(' ').AppendLine(type);
            builder.Append(name).Append(' ').AppendLine(value.ToString("0.###", CultureInfo.InvariantCulture));
        }

        private static object ToEndpointPayload(EndpointPerformanceSnapshot endpoint)
        {
            return new
            {
                method = endpoint.Method,
                path = endpoint.Path,
                totalRequests = endpoint.TotalRequests,
                clientErrors = endpoint.ClientErrorRequests,
                serverErrors = endpoint.ServerErrorRequests,
                totalDurationMs = endpoint.TotalDurationMs,
                averageDurationMs = endpoint.AverageDurationMs,
                minDurationMs = endpoint.MinDurationMs,
                maxDurationMs = endpoint.MaxDurationMs,
                lastDurationMs = endpoint.LastDurationMs,
                lastSeenUtc = endpoint.LastSeenUtc
            };
        }

        private sealed record DatabaseHealth(bool CanConnect, string Status, long LatencyMs);
    }
}
