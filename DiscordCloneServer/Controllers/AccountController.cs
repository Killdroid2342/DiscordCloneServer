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
                // Check if the username already exists
                if (_context.Accounts.Any(a => a.UserName == account.UserName))
                {
                    return new JsonResult(new { Error = "Username already exists." }) { StatusCode = StatusCodes.Status409Conflict };
                }


                _context.Accounts.Add(account);
            }
            else
            {
                var accountInDb = _context.Accounts.Find(account.Id);
                if (accountInDb == null)
                {
                    return new JsonResult(new { Error = "Account not found." }) { StatusCode = StatusCodes.Status404NotFound };
                }

                if (_context.Accounts.Any(a => a.UserName == account.UserName && a.Id != account.Id))
                {
                    return new JsonResult(new { Error = "Username already exists." }) { StatusCode = StatusCodes.Status409Conflict };
                }

                accountInDb.UserName = account.UserName;
            }

            // Save changes to the database
            _context.SaveChanges();
            return new JsonResult(account) { StatusCode = StatusCodes.Status200OK };
        }


    }
}
