using System.Configuration;
using DiscordCloneServer.Data;
using Microsoft.EntityFrameworkCore;

namespace DiscordCloneServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";

            // Add services to the container.
            builder.Services.AddCors(options =>
            {
                options.AddPolicy(name: MyAllowSpecificOrigins,
                    policy =>
                    {
                        policy.WithOrigins("http://127.0.0.1:5500").AllowAnyHeader();
                    });
            });
            builder.Services.AddDbContext<ApiContext>
                (opt => opt.UseSqlServer("Data Source=DESKTOP-28PDSSI\\FIRSTDB;Initial Catalog=DiscordClone;User ID=sa;Password=123456;Encrypt=False;TrustServerCertificate=True\r\n",
                opt => opt.EnableRetryOnFailure()));

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

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }

    }
}
