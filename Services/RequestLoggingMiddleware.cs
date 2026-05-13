using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DiscordCloneServer.Services
{
    public sealed class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;
        private readonly MonitoringMetrics _metrics;
        private readonly int _slowRequestThresholdMs;

        public RequestLoggingMiddleware(
            RequestDelegate next,
            ILogger<RequestLoggingMiddleware> logger,
            MonitoringMetrics metrics,
            IConfiguration configuration)
        {
            _next = next;
            _logger = logger;
            _metrics = metrics;
            _slowRequestThresholdMs = Math.Max(
                1,
                configuration.GetValue("Monitoring:SlowRequestThresholdMs", 1000));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

            using var scope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["TraceId"] = traceId,
                ["RequestId"] = context.TraceIdentifier
            });

            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _metrics.RecordRequest(
                    StatusCodes.Status500InternalServerError,
                    stopwatch.ElapsedMilliseconds,
                    context.Request.Method,
                    GetSafePath(context.Request));
                _logger.LogError(
                    ex,
                    "Unhandled request failure {Method} {Path} after {ElapsedMs}ms",
                    context.Request.Method,
                    GetSafePath(context.Request),
                    stopwatch.ElapsedMilliseconds);
                throw;
            }

            stopwatch.Stop();
            var statusCode = context.Response.StatusCode;
            _metrics.RecordRequest(
                statusCode,
                stopwatch.ElapsedMilliseconds,
                context.Request.Method,
                GetSafePath(context.Request));

            var level = GetLogLevel(statusCode, stopwatch.ElapsedMilliseconds, _slowRequestThresholdMs);
            _logger.Log(
                level,
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms for {User}",
                context.Request.Method,
                GetSafePath(context.Request),
                statusCode,
                stopwatch.ElapsedMilliseconds,
                context.User.GetUsername() ?? "anonymous");
        }

        private static string GetSafePath(HttpRequest request)
        {
            return request.Path.HasValue ? request.Path.Value! : "/";
        }

        private static LogLevel GetLogLevel(int statusCode, long elapsedMilliseconds, int slowRequestThresholdMs)
        {
            if (statusCode >= StatusCodes.Status500InternalServerError)
            {
                return LogLevel.Error;
            }

            if (statusCode >= StatusCodes.Status400BadRequest || elapsedMilliseconds >= slowRequestThresholdMs)
            {
                return LogLevel.Warning;
            }

            return LogLevel.Information;
        }
    }
}
