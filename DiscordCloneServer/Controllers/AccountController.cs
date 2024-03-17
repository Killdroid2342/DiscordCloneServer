using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DiscordCloneServer.Controllers
{
    public class AccountController : Controller
    {
        public class Account
        {
            public string Id;
            public string UserName;
            public string Password;

        }

        // POST: AccountController/Create
        [HttpPost]
        [Route("/Account/Register")]

        public ActionResult Create([FromBody] Account model)
        {
            try
            {
                Console.WriteLine($"Received registration data: {model.Id}, {model.UserName}, {model.Password}");


                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                // Log error
                Console.WriteLine($"Error during registration: {ex.Message}");
                return View();
            }
        }


    }

}
