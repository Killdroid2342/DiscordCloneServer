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
                        policy.WithOrigins("http://127.0.0.1:5500", "https://localhost:7170")
                            .AllowCredentials()
                            .WithHeaders("Content-Type", "Accept", "Authorization");
                    });

            });

            builder.Services.AddDbContext<ApiContext>
                (opt => opt.UseSqlServer("Data Source=DESKTOP-28PDSSI\\FIRSTDB;Initial Catalog=DiscordClone;User ID=sa;Password=123456;Encrypt=False;TrustServerCertificate=True\r\n",
                opt => opt.EnableRetryOnFailure()));

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
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();


            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseCors(MyAllowSpecificOrigins);

            app.UseAuthentication();
            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }

    }
}
