using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DiscordCloneServer.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Account = DiscordCloneServer.Models.Account;

namespace DiscordCloneServer.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly ApiContext _context;
        private readonly IConfiguration _config;

        public AccountController(ApiContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }


        [HttpPost]
        public JsonResult CreateAccount(Account account)
        {
            if (account.Id == 0)
            {
                if (_context.Accounts.Any(a => a.UserName == account.UserName))
                {
                    return new JsonResult(new { message = "Username already exists." });
                }

                _context.Accounts.Add(account);
                _context.SaveChanges();
                return new JsonResult(new { message = "Created Account" });
            }
            else
            {
                var accountInDb = _context.Accounts.Find(account.Id);
                if (accountInDb == null)
                {
                    return new JsonResult(new { message = "Account not found." });
                }

                if (_context.Accounts.Any(a => a.UserName == account.UserName && a.Id != account.Id))
                {
                    return new JsonResult(new { message = "Username already exists." });
                }

                accountInDb.UserName = account.UserName;
            }

            _context.SaveChanges();
            return new JsonResult(account);
        }
        [HttpPost]
        public JsonResult LogIn(Account account)
        {
            try
            {
                if (_context.Accounts.Any(a => a.UserName == account.UserName && a.PassWord == account.PassWord))
                {
                    Console.WriteLine("login worked");
                    var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
                    var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

                    var claims = new List<Claim>
                    {
                        new Claim("username", account.UserName)
                    };

                    var token = new JwtSecurityToken(
                        _config["Jwt:Issuer"],
                        null,
                        claims: claims,
                        expires: DateTime.Now.AddDays(14),
                        signingCredentials: credentials
                    );

                    var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
                    Console.WriteLine($"made token: {tokenString}");

                    return new JsonResult(new { message = "Correct Details", token = tokenString });

                }
                else
                {
                    Console.WriteLine("wrong login info");
                    return new JsonResult(new { message = "Wrong Details" });
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"login broke: {e}");
            }
            return new JsonResult(account);
        }


        [HttpPost]
        public JsonResult VerifyToken(string token)
        {
            try
            {
                Console.WriteLine($"checking token: {token}");
                var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]));

                var tokenHandler = new JwtSecurityTokenHandler();
                Console.WriteLine($"token handler: {tokenHandler}");
                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = _config["Jwt:Issuer"],
                    IssuerSigningKey = securityKey
                }, out var validatedToken);

                Console.WriteLine("token is good");
                return new JsonResult(new { message = "Token is correct." });
            }
            catch (Exception)
            {
                Console.WriteLine("token is bad");
                return new JsonResult(new { message = "Token is not correct." });
            }
        }

        [HttpPost]
        public JsonResult AddFriend(string username, string friendUsername)
        {
            try
            {
                var userAccount = _context.Accounts.FirstOrDefault(a => a.UserName == username);
                var friendAccount = _context.Accounts.FirstOrDefault(a => a.UserName == friendUsername);
                if (userAccount == null || friendAccount == null)
                {
                    return new JsonResult(new { message = "Account not found." });
                }

                if (userAccount.Friends != null && userAccount.Friends.Contains(friendUsername))
                {
                    return new JsonResult(new { message = "Friend already added." });
                }

                if (userAccount.Friends == null)
                {
                    userAccount.Friends = [friendUsername];
                }
                else
                {
                    var updatedUserFriendsList = userAccount.Friends.ToList();
                    updatedUserFriendsList.Add(friendUsername);
                    userAccount.Friends = updatedUserFriendsList.ToArray();
                }

                if (friendAccount.Friends == null)
                {
                    friendAccount.Friends = [username];
                }
                else
                {
                    var updatedFriendFriendsList = friendAccount.Friends.ToList();
                    updatedFriendFriendsList.Add(username);
                    friendAccount.Friends = updatedFriendFriendsList.ToArray();
                }

                _context.SaveChanges();

                return new JsonResult(new { message = "Friend added successfully." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"couldnt add friend: {ex.Message}");
                return new JsonResult(new { message = "Error adding friend." });
            }
        }
        [HttpGet]
        public JsonResult GetFriends(string username)
        {
            try
            {
                var account = _context.Accounts.FirstOrDefault(account => account.UserName == username);

                if (account != null)
                {
                    if (!account.Friends.Any())
                        return new JsonResult("No Friends Added!");

                    return new JsonResult(account.Friends);
                }
                else
                {
                    return new JsonResult("User not found");
                }

            }
            catch (Exception ex)
            {
                return new JsonResult("Internal server error");
            }
        }

        [HttpPost]
        public JsonResult RemoveFriend(string username, string friendUsername)
        {
            try
            {
                var userAccount = _context.Accounts.FirstOrDefault(a => a.UserName == username);
                var friendAccount = _context.Accounts.FirstOrDefault(a => a.UserName == friendUsername);

                if (userAccount == null || friendAccount == null)
                {
                    return new JsonResult(new { message = "Account not found." });
                }

                if (userAccount.Friends != null && userAccount.Friends.Contains(friendUsername))
                {
                    var userFriendsList = userAccount.Friends.ToList();
                    userFriendsList.Remove(friendUsername);
                    userAccount.Friends = userFriendsList.ToArray();

                    if (friendAccount.Friends != null && friendAccount.Friends.Contains(username))
                    {
                        var friendFriendsList = friendAccount.Friends.ToList();
                        friendFriendsList.Remove(username);
                        friendAccount.Friends = friendFriendsList.ToArray();
                    }

                    _context.SaveChanges();

                    return new JsonResult(new { message = "Friend removed successfully." });
                }
                else
                {
                    return new JsonResult(new { message = "Friend not found in user's friend list." });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"couldnt remove friend: {ex.Message}");
                return new JsonResult(new { message = "Error removing friend." });
            }
        }
    }
}

