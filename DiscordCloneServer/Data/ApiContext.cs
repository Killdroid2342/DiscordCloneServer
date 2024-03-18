using DiscordCloneServer.Models;
using Microsoft.EntityFrameworkCore;

namespace DiscordCloneServer.Data
{
    public class ApiContext : DbContext
    {
        public DbSet<RegisterAccount> Accounts { get; set; }
        public ApiContext(DbContextOptions<ApiContext> options)
             : base(options)
        {

        }
    }
}
