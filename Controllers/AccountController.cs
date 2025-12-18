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
                if (username == friendUsername)
                {
                    return new JsonResult(new { message = "You cannot add yourself as a friend." });
                }

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

                if (userAccount.OutgoingFriendRequests != null && userAccount.OutgoingFriendRequests.Contains(friendUsername))
                {
                    return new JsonResult(new { message = "Friend request already sent." });
                }

                if (userAccount.IncomingFriendRequests != null && userAccount.IncomingFriendRequests.Contains(friendUsername))
                {
                    return new JsonResult(new { message = "This user has already sent you a friend request. Please accept it." });
                }


                var outgoingList = userAccount.OutgoingFriendRequests?.ToList() ?? new List<string>();
                outgoingList.Add(friendUsername);
                userAccount.OutgoingFriendRequests = outgoingList.ToArray();


                var incomingList = friendAccount.IncomingFriendRequests?.ToList() ?? new List<string>();
                incomingList.Add(username);
                friendAccount.IncomingFriendRequests = incomingList.ToArray();

                _context.SaveChanges();

                return new JsonResult(new { message = "Friend request sent successfully." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"couldnt add friend request: {ex.Message}");
                return new JsonResult(new { message = "Error sending friend request." });
            }
        }

        [HttpPost]
        public JsonResult AcceptFriendRequest(string username, string friendUsername)
        {
            try
            {
                var userAccount = _context.Accounts.FirstOrDefault(a => a.UserName == username);
                var friendAccount = _context.Accounts.FirstOrDefault(a => a.UserName == friendUsername);

                if (userAccount == null || friendAccount == null) return new JsonResult(new { message = "Account not found" });


                if (userAccount.IncomingFriendRequests == null || !userAccount.IncomingFriendRequests.Contains(friendUsername))
                {
                    return new JsonResult(new { message = "No friend request found from this user." });
                }


                var userIncoming = userAccount.IncomingFriendRequests.ToList();
                userIncoming.Remove(friendUsername);
                userAccount.IncomingFriendRequests = userIncoming.ToArray();

                var friendOutgoing = friendAccount.OutgoingFriendRequests?.ToList() ?? new List<string>();
                friendOutgoing.Remove(username);
                friendAccount.OutgoingFriendRequests = friendOutgoing.ToArray();


                var userFriends = userAccount.Friends?.ToList() ?? new List<string>();
                userFriends.Add(friendUsername);
                userAccount.Friends = userFriends.ToArray();

                var friendFriends = friendAccount.Friends?.ToList() ?? new List<string>();
                friendFriends.Add(username);
                friendAccount.Friends = friendFriends.ToArray();

                _context.SaveChanges();
                return new JsonResult(new { message = "Friend request accepted." });
            }
            catch (Exception ex)
            {
                 Console.WriteLine($"Error accepting friend: {ex.Message}");
                 return new JsonResult(new { message = "Error accepting friend request." });
            }
        }

        [HttpPost]
        public JsonResult DeclineFriendRequest(string username, string friendUsername)
        {
            try
            {
                var userAccount = _context.Accounts.FirstOrDefault(a => a.UserName == username);
                var friendAccount = _context.Accounts.FirstOrDefault(a => a.UserName == friendUsername);

                if (userAccount == null || friendAccount == null) return new JsonResult(new { message = "Account not found" });


                if (userAccount.IncomingFriendRequests != null && userAccount.IncomingFriendRequests.Contains(friendUsername))
                {
                    var userIncoming = userAccount.IncomingFriendRequests.ToList();
                    userIncoming.Remove(friendUsername);
                    userAccount.IncomingFriendRequests = userIncoming.ToArray();
                }

                if (friendAccount.OutgoingFriendRequests != null && friendAccount.OutgoingFriendRequests.Contains(username))
                {
                    var friendOutgoing = friendAccount.OutgoingFriendRequests.ToList();
                    friendOutgoing.Remove(username);
                    friendAccount.OutgoingFriendRequests = friendOutgoing.ToArray();
                }

                _context.SaveChanges();
                return new JsonResult(new { message = "Friend request declined." });
            }
            catch (Exception ex)
            {
                 Console.WriteLine($"Error declining friend: {ex.Message}");
                 return new JsonResult(new { message = "Error declining friend request." });
            }
        }

        [HttpGet]
        public JsonResult GetFriendRequests(string username)
        {
            try
            {
                var account = _context.Accounts.FirstOrDefault(a => a.UserName == username);
                if (account == null) return new JsonResult("User not found");

                return new JsonResult(account.IncomingFriendRequests ?? new string[0]);
            }
            catch (Exception ex)
            {
                return new JsonResult("Internal server error");
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

