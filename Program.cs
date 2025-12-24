using System.Configuration;
using System.Text;
using DiscordCloneServer.Data;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace DiscordCloneServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";


            builder.Services.AddCors(options =>
            {
                options.AddPolicy(name: MyAllowSpecificOrigins,
                    policy =>
                    {
                        policy.WithOrigins("http://127.0.0.1:5500", "https://localhost:7170", "http://127.0.0.1:8080", "http://localhost:8080")
                            .AllowCredentials()
                            .AllowAnyHeader()
                            .AllowAnyMethod()
                            .SetIsOriginAllowed(_ => true);
                    });

            });

            builder.Services.AddDbContext<ApiContext>
                (opt => opt.UseSqlServer(
                    "Server=localhost\\SQLEXPRESS;Database=DiscordClone;Trusted_Connection=True;Encrypt=False;TrustServerCertificate=True",
                    opt => opt.EnableRetryOnFailure()
                ));

            var jwtIssuer = builder.Configuration.GetSection("Jwt:Issuer").Get<string>();
            var jwtKey = builder.Configuration.GetSection("Jwt:Key").Get<string>();

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
                     IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
                 };
             });
            builder.Services.AddControllers();

            
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
                Console.WriteLine($"server startup failed: {ex.Message}");
                Console.WriteLine($"error info: {ex.StackTrace}");
                throw;
            }



        }

    }
}
