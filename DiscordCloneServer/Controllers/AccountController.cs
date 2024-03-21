using DiscordCloneServer.Data;
using DiscordCloneServer.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DiscordCloneServer.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly ApiContext _context;
        public AccountController(ApiContext context)
        {
            _context = context;
        }

        // create/edit
        [HttpPost]
        public JsonResult CreateAccount(Account account)
        {
            if (account.Id == 0)
            {
                _context.Accounts.Add(account);
                Console.WriteLine("this is the account");
                Console.WriteLine(account);
                Console.WriteLine(_context);
            }
            else
            {
                var accountInDb = _context.Accounts.Find(account.Id);
                if (accountInDb == null) return new JsonResult(NotFound());
                accountInDb = account;
            }
            _context.SaveChanges();
            return new JsonResult(Ok(account));
        }

    }
}
