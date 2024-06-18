
using Microsoft.AspNetCore.Mvc;

namespace DiscordCloneServer.Controllers
{
    public class SignalingController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
