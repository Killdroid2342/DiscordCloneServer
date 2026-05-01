using System.Text;
using DiscordCloneServer.Data;
using DiscordCloneServer.Services;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

namespace DiscordCloneServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            const string MyAllowSpecificOrigins = "_myAllowSpecificOrigins";
            var allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "http://127.0.0.1:5500",
                "http://127.0.0.1:3300",
                "https://localhost:7170",
                "http://127.0.0.1:8080",
                "http://localhost:8080",
                "null"
            };
            var configuredOrigins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>() ?? Array.Empty<string>();
            var configuredOriginsCsv = builder.Configuration["Cors:AllowedOriginsCsv"] ?? string.Empty;
            foreach (var origin in configuredOrigins.Concat(configuredOriginsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries)))
            {
                var normalizedOrigin = origin.Trim();
                if (!string.IsNullOrWhiteSpace(normalizedOrigin))
                {
                    allowedOrigins.Add(normalizedOrigin);
                }
            }


            builder.Services.AddCors(options =>
            {
                options.AddPolicy(name: MyAllowSpecificOrigins,
                    policy =>
                    {
                        policy.WithOrigins(allowedOrigins.Where(origin => origin != "null").ToArray())
                            .AllowCredentials()
                            .AllowAnyHeader()
                            .AllowAnyMethod()
                            .SetIsOriginAllowed(origin => IsAllowedCorsOrigin(origin, allowedOrigins));
                    });

            });

            builder.Services.AddDbContext<ApiContext>
                (opt => opt.UseSqlServer(
                    builder.Configuration.GetConnectionString("DefaultConnection"),
                    opt => opt.EnableRetryOnFailure()
                ));

            var jwtIssuer = builder.Configuration.GetSection("Jwt:Issuer").Get<string>();
            var jwtKey = builder.Configuration.GetSection("Jwt:Key").Get<string>();
            if (string.IsNullOrWhiteSpace(jwtIssuer) || string.IsNullOrWhiteSpace(jwtKey))
            {
                throw new InvalidOperationException("Jwt:Issuer and Jwt:Key must be configured. Store Jwt:Key in user secrets or the JWT__KEY environment variable, not appsettings.json.");
            }

            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
             .AddJwtBearer(options =>
             {
                 options.TokenValidationParameters = new TokenValidationParameters
                 {
                     ValidateIssuer = true,
                     ValidateAudience = true,
                     ValidateLifetime = true,
                     ValidateIssuerSigningKey = true,
                     ValidIssuer = jwtIssuer,
                     ValidAudience = jwtIssuer,
                     IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                     ClockSkew = TimeSpan.FromMinutes(2)
                 };
                 options.Events = new JwtBearerEvents
                 {
                     OnMessageReceived = context =>
                     {
                         if (string.IsNullOrWhiteSpace(context.Token) &&
                             context.Request.Query.TryGetValue("access_token", out var accessToken))
                         {
                             context.Token = accessToken;
                         }

                         if (string.IsNullOrWhiteSpace(context.Token) &&
                             context.Request.Cookies.TryGetValue("token", out var cookieToken))
                         {
                             context.Token = cookieToken;
                         }

                         return Task.CompletedTask;
                     },
                     OnTokenValidated = async context =>
                     {
                         var username = context.Principal?.GetUsername();
                         var sessionId = context.Principal?.GetSessionId();
                         if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(sessionId))
                         {
                             context.Fail("Missing session claim.");
                             return;
                         }

                         var db = context.HttpContext.RequestServices.GetRequiredService<ApiContext>();
                         var session = await db.AccountSessions
                             .FirstOrDefaultAsync(s => s.Id == sessionId && s.Username == username);
                         var account = await db.Accounts
                             .FirstOrDefaultAsync(a => a.UserName == username);

                         if (session == null || account == null || account.IsDisabled ||
                             session.RevokedAt != null || session.ExpiresAt <= DateTime.UtcNow)
                         {
                             context.Fail("Session is no longer active.");
                             return;
                         }

                         session.LastSeenAt = DateTime.UtcNow;
                         await db.SaveChangesAsync();
                     }
                 };
             });
            builder.Services.AddControllers();
            builder.Services.AddHttpClient<IContactVerificationDelivery, ContactVerificationDelivery>();
            builder.Services.AddHttpClient<IEmailNotificationSender, EmailNotificationSender>();
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.ContentType = "application/json";
                    await context.HttpContext.Response.WriteAsync(
                        "{\"message\":\"Too many requests. Please slow down and try again shortly.\"}",
                        cancellationToken);
                };

                options.AddPolicy("auth", httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetClientPartitionKey(httpContext, "auth"),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 8,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        }));

                options.AddPolicy("friend", httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetClientPartitionKey(httpContext, "friend"),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 30,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        }));

                options.AddPolicy("upload", httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetClientPartitionKey(httpContext, "upload"),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 12,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        }));

                options.AddPolicy("abuse", httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetClientPartitionKey(httpContext, "abuse"),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 120,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        }));
            });

            
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddSignalR();

            try
            {
                var app = builder.Build();
                app.UseWebSockets();
                
                string wwwrootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
                if (!Directory.Exists(wwwrootPath))
                {
                    Directory.CreateDirectory(wwwrootPath);
                }

                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(wwwrootPath),
                    RequestPath = ""
                });

                if (app.Environment.IsDevelopment())
                {
                    app.UseSwagger();
                    app.UseSwaggerUI();
                }


                app.UseCors(MyAllowSpecificOrigins);

                app.UseAuthentication();
                app.UseRateLimiter();
                app.UseAuthorization();

                using (var scope = app.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApiContext>();

                    db.Database.Migrate();
                }

                app.MapControllers();
                app.MapHub<Hubs.ChatHub>("/chatHub");

                Console.WriteLine("starting discord server on http://localhost:5018");
                app.Run();
            }
            catch (Exception ex)
            {
                if (ex.GetType().Name == "HostAbortedException")
                {
                    throw;
                }

                Console.WriteLine($"server startup failed: {ex.Message}");
                Console.WriteLine($"error info: {ex.StackTrace}");
                throw;
            }



        }

        private static bool IsAllowedCorsOrigin(string? origin, ISet<string> allowedOrigins)
        {
            if (string.IsNullOrWhiteSpace(origin) || allowedOrigins.Contains(origin))
            {
                return true;
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var isLoopbackHost =
                uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase);

            return isLoopbackHost && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        private static string GetClientPartitionKey(HttpContext httpContext, string policyName)
        {
            var username = httpContext.User.GetUsername();
            if (!string.IsNullOrWhiteSpace(username))
            {
                return $"{policyName}:user:{username}";
            }

            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return $"{policyName}:ip:{ipAddress}";
        }

    }
}
