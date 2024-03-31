﻿using System;
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

                }
                else
                {
                    Console.WriteLine("Wrong Details");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            return new JsonResult(account);
        }


    }
}
