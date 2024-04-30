using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DiscordCloneServer.Data;
using DiscordCloneServer.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

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

        // create/edit
        [HttpPost]
        public JsonResult CreateAccount(Account account)
        {
            if (account.Id == 0)
            {
                // Check if the username already exists
                if (_context.Accounts.Any(a => a.UserName == account.UserName))
                {
                    return new JsonResult(new { message = "Username already exists." });
                }


                _context.Accounts.Add(account);
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

            // Save changes to the database
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
                    Console.WriteLine("Correct Details");
                    var securityKey = new SymmetricSecurityKey(Encoding.UTF32.GetBytes(_config["Jwt:Key"]));
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
                    Console.WriteLine(tokenString);

                    return new JsonResult(new { message = "Correct Details", token = tokenString });

                }
                else
                {
                    Console.WriteLine("Wrong Details");
                    return new JsonResult(new { message = "Wrong Details" });
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            return new JsonResult(account);
        }


        [HttpPost]
        public JsonResult VerifyToken(string token)
        {
            try
            {
                Console.WriteLine(token);
                var securityKey = new SymmetricSecurityKey(Encoding.UTF32.GetBytes(_config["Jwt:Key"]));

                var tokenHandler = new JwtSecurityTokenHandler();
                Console.WriteLine(tokenHandler);
                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = _config["Jwt:Issuer"],
                    IssuerSigningKey = securityKey
                }, out var validatedToken);

                Console.WriteLine("Token is correct.");
                return new JsonResult(new { message = "Token is correct." });
            }
            catch (Exception)
            {
                Console.WriteLine("Token is not correct.");
                return new JsonResult(new { message = "Token is not correct." });
            }
        }


    }
}
