using DiscordCloneServer.Data;
using Microsoft.EntityFrameworkCore;

namespace DiscordCloneServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddDbContext<ApiContext>
                (opt => opt.UseSqlServer("Data Source=DESKTOP-28PDSSI\\FIRSTDB;Initial Catalog=DiscordClone;User ID=sa;Password=123456;Encrypt=False;TrustServerCertificate=True",
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

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }

    }
}
