using Microsoft.EntityFrameworkCore;

namespace DiscordCloneServer
{
    public class Program
    {
        static readonly string connectionString = "Data Source=DESKTOP-28PDSSI\\DiscordClone;Initial Catalog=DiscordClone;User ID=sa;Password=123456;Encrypt=False;TrustServerCertificate=True";
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            ConfirgureServices(builder.Services);
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
        private static void ConfirgureServices(IServiceCollection services)
        {

            services.AddDbContext<DiscordContext>(opt => opt.UseSqlServer(connectionString));
            services.AddControllers();
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen();
            // services.AddMvc().AddnewtosoftJSON();

        }
    }
}
