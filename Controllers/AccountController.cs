using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DiscordCloneServer.Data;
using DiscordCloneServer.Models;
using DiscordCloneServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Account = DiscordCloneServer.Models.Account;

namespace DiscordCloneServer.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    [Authorize]
    public class AccountController : ControllerBase
    {
        private const int AccessTokenHours = 24;
        private const int RefreshTokenDays = 30;
        private const int PasswordResetMinutes = 30;
        private const int TwoFactorLoginMinutes = 5;
        private const int BackupCodeCount = 10;
        private const int BackupCodeCharacters = 10;
        private const int MaxSettingsJsonBytes = 32768;
        private const int MaxCustomStatusLength = 128;
        private const int MaxActivityStatusLength = 120;
        private const int MaxUserBioLength = 280;
        private const int MaxProfileBadgeCount = 6;
        private const string CustomStatusSettingsKey = "customStatus";
        private const string ProfileBadgesSettingsKey = "profileBadges";
        private const string DeletedUserName = "Deleted User";
        private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        private static readonly Regex UsernameRegex = new("^[A-Za-z0-9_.-]{3,32}$", RegexOptions.Compiled);
        private static readonly Regex PhoneRegex = new(@"^\+?[0-9 .()\-]{7,32}$", RegexOptions.Compiled);
        private static readonly HashSet<string> PresenceStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "online",
            "idle",
            "do-not-disturb",
            "invisible"
        };
        private static readonly HashSet<string> DmPolicies = new(StringComparer.OrdinalIgnoreCase)
        {
            "everyone",
            "friends",
            "none"
        };
        private static readonly HashSet<string> AccountStandings = new(StringComparer.OrdinalIgnoreCase)
        {
            "good",
            "limited",
            "at-risk",
            "suspended"
        };
        private static readonly HashSet<string> ProfileBadgeIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "early-member",
            "community-helper",
            "server-builder",
            "bug-hunter",
            "developer",
            "artist",
            "gamer",
            "music-fan"
        };

        private readonly ApiContext _context;
        private readonly IConfiguration _config;
        private readonly IContactVerificationDelivery? _verificationDelivery;

        public AccountController(
            ApiContext context,
            IConfiguration config,
            IContactVerificationDelivery? verificationDelivery = null)
        {
            _context = context;
            _config = config;
            _verificationDelivery = verificationDelivery;
        }

        [HttpPost]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public IActionResult CreateAccount([FromBody] Account account)
        {
            try
            {
                if (account.Id != 0)
                {
                    return BadRequest(new { message = "Use the authenticated profile endpoints to update an account." });
                }

                var username = NormalizeUsername(account.UserName);
                var password = account.PassWord?.Trim() ?? string.Empty;

                if (!IsValidUsername(username))
                {
                    return BadRequest(new { message = "Username must be 3-32 characters and use only letters, numbers, dots, underscores, or hyphens." });
                }

                if (!IsValidPassword(password))
                {
                    return BadRequest(new { message = "Password must be 8-128 characters and include at least one letter and one number." });
                }

                if (UsernameExists(username))
                {
                    return Conflict(new { message = "Username already exists." });
                }

                account.UserName = username;
                account.PassWord = PasswordHasher.HashPassword(password);
                account.CreatedAt = DateTime.UtcNow;
                account.PasswordUpdatedAt = DateTime.UtcNow;
                account.Friends = Array.Empty<string>();
                account.IncomingFriendRequests = Array.Empty<string>();
                account.OutgoingFriendRequests = Array.Empty<string>();
                account.Groups = Array.Empty<string>();
                account.BlockedUsers = Array.Empty<string>();
                account.PresenceStatus = "online";
                account.ActivityStatus = string.Empty;
                account.LastActiveAt = DateTime.UtcNow;
                account.AccountStanding = "good";
                account.TrustScore = 60;
                account.StandingReason = null;
                account.StandingUpdatedAt = DateTime.UtcNow;
                account.PrivacyDmPolicy = "friends";
                account.PrivacyAllowFriendRequestsEveryone = true;
                account.PrivacyAllowFriendRequestsFriendsOfFriends = true;
                account.PrivacyAllowFriendRequestsServerMembers = true;
                account.PrivacyShowActivity = true;
                account.SettingsJson = "{}";
                account.VoiceChangerSettingsJson = "{}";
                account.IsDisabled = false;
                account.PasswordResetTokenHash = null;
                account.PasswordResetExpiresAt = null;
                account.TwoFactorEnabled = false;
                account.AuthenticatorSecretProtected = null;
                account.TwoFactorBackupCodeHashes = Array.Empty<string>();
                account.TwoFactorLoginTicketHash = null;
                account.TwoFactorLoginTicketExpiresAt = null;

                _context.Accounts.Add(account);
                _context.SaveChanges();
                return Ok(new { message = "Created Account" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CreateAccount broke: {ex}");
                return StatusCode(500, new { message = "Database connection error." });
            }
        }

        [HttpPost]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public IActionResult LogIn([FromBody] Account account)
        {
            try
            {
                var username = NormalizeUsername(account.UserName);
                var suppliedPassword = account.PassWord ?? string.Empty;
                var accountInDb = _context.Accounts.FirstOrDefault(a => a.UserName == username);

                if (accountInDb == null)
                {
                    return Unauthorized(new { message = "Wrong Details" });
                }

                var passwordResult = PasswordHasher.VerifyPassword(accountInDb.PassWord, suppliedPassword);
                if (passwordResult == PasswordVerificationResult.Failed)
                {
                    return Unauthorized(new { message = "Wrong Details" });
                }

                if (passwordResult == PasswordVerificationResult.SuccessRehashNeeded)
                {
                    accountInDb.PassWord = PasswordHasher.HashPassword(suppliedPassword);
                    accountInDb.PasswordUpdatedAt = DateTime.UtcNow;
                }

                var wasDisabled = accountInDb.IsDisabled;
                if (wasDisabled)
                {
                    accountInDb.IsDisabled = false;
                }

                accountInDb.PasswordResetTokenHash = null;
                accountInDb.PasswordResetExpiresAt = null;

                if (accountInDb.TwoFactorEnabled)
                {
                    var ticket = GenerateOpaqueToken();
                    accountInDb.TwoFactorLoginTicketHash = HashToken(ticket);
                    accountInDb.TwoFactorLoginTicketExpiresAt = DateTime.UtcNow.AddMinutes(TwoFactorLoginMinutes);
                    _context.SaveChanges();

                    return Ok(new
                    {
                        message = "Two-factor authentication required.",
                        twoFactorRequired = true,
                        twoFactorTicket = ticket,
                        expiresAt = accountInDb.TwoFactorLoginTicketExpiresAt,
                        username = accountInDb.UserName
                    });
                }

                var login = CreateSession(accountInDb);
                _context.SaveChanges();
                SetRefreshCookie(login.RefreshToken);

                return Ok(new
                {
                    message = wasDisabled ? "Account re-enabled." : "Correct Details",
                    token = login.AccessToken,
                    refreshToken = login.RefreshToken,
                    expiresAt = login.AccessTokenExpiresAt,
                    sessionId = login.SessionId
                });
            }
            catch (Exception e)
            {
                Console.WriteLine($"login broke: {e}");
                return StatusCode(500, new { message = "Database connection error." });
            }
        }

        [HttpPost]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public IActionResult CompleteTwoFactorLogin([FromBody] TwoFactorLoginRequest request)
        {
            try
            {
                var username = NormalizeUsername(request.Username);
                var account = _context.Accounts.FirstOrDefault(a => a.UserName == username);
                if (account == null ||
                    account.IsDisabled ||
                    !account.TwoFactorEnabled ||
                    string.IsNullOrWhiteSpace(request.TwoFactorTicket) ||
                    string.IsNullOrWhiteSpace(account.TwoFactorLoginTicketHash) ||
                    account.TwoFactorLoginTicketExpiresAt <= DateTime.UtcNow ||
                    !FixedTimeEquals(account.TwoFactorLoginTicketHash, HashToken(request.TwoFactorTicket)))
                {
                    return Unauthorized(new { message = "Two-factor challenge is invalid or expired." });
                }

                if (!TryValidateTwoFactorCode(account, request.Code, consumeBackupCode: true))
                {
                    return Unauthorized(new { message = "Two-factor code is invalid." });
                }

                account.TwoFactorLoginTicketHash = null;
                account.TwoFactorLoginTicketExpiresAt = null;

                var login = CreateSession(account);
                _context.SaveChanges();
                SetRefreshCookie(login.RefreshToken);

                return Ok(new
                {
                    message = "Correct Details",
                    token = login.AccessToken,
                    refreshToken = login.RefreshToken,
                    expiresAt = login.AccessTokenExpiresAt,
                    sessionId = login.SessionId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"2fa login failed: {ex}");
                return StatusCode(500, new { message = "Could not complete two-factor login." });
            }
        }

        [HttpPost]
        [AllowAnonymous]
        public IActionResult VerifyToken(string? token = null)
        {
            try
            {
                token = string.IsNullOrWhiteSpace(token) ? ReadAccessTokenFromRequest() : token;
                if (string.IsNullOrWhiteSpace(token))
                {
                    return Unauthorized(new { message = "Token is not correct." });
                }

                var validation = ValidateAccessTokenAndSession(token);
                if (!validation.IsValid)
                {
                    return Unauthorized(new { message = "Token is not correct." });
                }

                return Ok(new { message = "Token is correct." });
            }
            catch
            {
                return Unauthorized(new { message = "Token is not correct." });
            }
        }

        [HttpPost]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public IActionResult Refresh([FromBody] RefreshTokenRequest? request = null)
        {
            try
            {
                var refreshToken = request?.RefreshToken;
                if (string.IsNullOrWhiteSpace(refreshToken))
                {
                    refreshToken = HttpContext?.Request.Cookies["refreshToken"];
                }

                if (string.IsNullOrWhiteSpace(refreshToken))
                {
                    return Unauthorized(new { message = "Refresh token is required." });
                }

                var refreshHash = HashToken(refreshToken);
                var session = _context.AccountSessions
                    .FirstOrDefault(s => s.RefreshTokenHash == refreshHash);

                if (session == null || session.RevokedAt != null || session.ExpiresAt <= DateTime.UtcNow)
                {
                    return Unauthorized(new { message = "Session is no longer active." });
                }

                var account = _context.Accounts.FirstOrDefault(a => a.Id == session.AccountId && a.UserName == session.Username);
                if (account == null || account.IsDisabled)
                {
                    return Unauthorized(new { message = "Account is no longer active." });
                }

                var replacement = CreateSession(account);
                session.RevokedAt = DateTime.UtcNow;
                session.ReplacedBySessionId = replacement.SessionId;
                _context.SaveChanges();
                SetRefreshCookie(replacement.RefreshToken);

                return Ok(new
                {
                    message = "Session refreshed.",
                    token = replacement.AccessToken,
                    refreshToken = replacement.RefreshToken,
                    expiresAt = replacement.AccessTokenExpiresAt,
                    sessionId = replacement.SessionId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"refresh failed: {ex}");
                return StatusCode(500, new { message = "Could not refresh session." });
            }
        }

        [HttpPost]
        public IActionResult Logout()
        {
            try
            {
                RevokeCurrentSession();
                ClearAuthCookies();
                _context.SaveChanges();
                return Ok(new { message = "Logged out." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"logout failed: {ex}");
                return StatusCode(500, new { message = "Could not log out." });
            }
        }

        [HttpPost]
        public IActionResult LogoutAll()
        {
            try
            {
                var username = GetCurrentUsername();
                if (username == null)
                {
                    return Unauthorized(new { message = "Missing user identity." });
                }

                RevokeSessionsForUsername(username);
                ClearAuthCookies();
                _context.SaveChanges();
                return Ok(new { message = "All sessions have been logged out." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"logout all failed: {ex}");
                return StatusCode(500, new { message = "Could not log out sessions." });
            }
        }

        [HttpGet]
        public IActionResult GetSessions()
        {
            var username = GetCurrentUsername();
            if (username == null)
            {
                return Unauthorized(new { message = "Missing user identity." });
            }

            var currentSessionId = User.GetSessionId();
            var sessions = _context.AccountSessions
                .Where(s => s.Username == username && s.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(s => s.LastSeenAt)
                .Select(s => new
                {
                    s.Id,
                    s.CreatedAt,
                    s.ExpiresAt,
                    s.LastSeenAt,
                    s.RevokedAt,
                    s.UserAgent,
                    s.IpAddress,
                    IsCurrent = s.Id == currentSessionId,
                    IsActive = s.RevokedAt == null
                })
                .ToList();

            return Ok(sessions);
        }

        [HttpPost]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public IActionResult RequestPasswordReset([FromBody] PasswordResetRequest request)
        {
            try
            {
                var username = NormalizeUsername(request.Username);
                var account = _context.Accounts.FirstOrDefault(a => a.UserName == username && !a.IsDisabled);
                string? resetToken = null;

                if (account != null)
                {
                    resetToken = GenerateOpaqueToken();
                    account.PasswordResetTokenHash = HashToken(resetToken);
                    account.PasswordResetExpiresAt = DateTime.UtcNow.AddMinutes(PasswordResetMinutes);
                    _context.SaveChanges();
                }

                var response = new Dictionary<string, object?>
                {
                    ["message"] = "If that account exists, a password reset token has been created."
                };

                if (resetToken != null && _config.GetValue<bool>("Account:ExposePasswordResetToken"))
                {
                    response["resetToken"] = resetToken;
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"password reset request failed: {ex}");
                return StatusCode(500, new { message = "Could not request a password reset." });
            }
        }

        [HttpPost]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public IActionResult ResetPassword([FromBody] ResetPasswordRequest request)
        {
            try
            {
                var username = NormalizeUsername(request.Username);
                var account = _context.Accounts.FirstOrDefault(a => a.UserName == username && !a.IsDisabled);
                if (account == null ||
                    string.IsNullOrWhiteSpace(request.Token) ||
                    string.IsNullOrWhiteSpace(account.PasswordResetTokenHash) ||
                    account.PasswordResetExpiresAt <= DateTime.UtcNow ||
                    !FixedTimeEquals(account.PasswordResetTokenHash, HashToken(request.Token)))
                {
                    return BadRequest(new { message = "Password reset token is invalid or expired." });
                }

                if (!IsValidPassword(request.NewPassword))
                {
                    return BadRequest(new { message = "Password must be 8-128 characters and include at least one letter and one number." });
                }

                account.PassWord = PasswordHasher.HashPassword(request.NewPassword);
                account.PasswordUpdatedAt = DateTime.UtcNow;
                account.PasswordResetTokenHash = null;
                account.PasswordResetExpiresAt = null;
                RevokeSessionsForUsername(account.UserName);
                _context.SaveChanges();

                return Ok(new { message = "Password reset successfully. Please log in again." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"password reset failed: {ex}");
                return StatusCode(500, new { message = "Could not reset password." });
            }
        }

        [HttpPost]
        public IActionResult ChangePassword([FromBody] ChangePasswordRequest request)
        {
            try
            {
                var account = GetCurrentAccount();
                if (account == null)
                {
                    return NotFound(new { message = "Account not found." });
                }

                if (PasswordHasher.VerifyPassword(account.PassWord, request.CurrentPassword) == PasswordVerificationResult.Failed)
                {
                    return BadRequest(new { message = "Current password is incorrect." });
                }

                if (!IsValidPassword(request.NewPassword))
                {
                    return BadRequest(new { message = "New password must be 8-128 characters and include at least one letter and one number." });
                }

                if (request.CurrentPassword == request.NewPassword)
                {
                    return BadRequest(new { message = "Use a new password that is different from the current one." });
                }

                account.PassWord = PasswordHasher.HashPassword(request.NewPassword);
                account.PasswordUpdatedAt = DateTime.UtcNow;
                account.PasswordResetTokenHash = null;
                account.PasswordResetExpiresAt = null;
                RevokeOtherSessionsForCurrentUser();
                _context.SaveChanges();

                return Ok(new { message = "Password changed successfully." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"change password failed: {ex}");
                return StatusCode(500, new { message = "Could not change password." });
            }
        }

        [HttpPost]
        public IActionResult DisableAccount([FromBody] AccountPasswordRequest request)
        {
            try
            {
                var account = GetCurrentAccount();
                if (account == null)
                {
                    return NotFound(new { message = "Account not found." });
                }

                if (PasswordHasher.VerifyPassword(account.PassWord, request.Password) == PasswordVerificationResult.Failed)
                {
                    return BadRequest(new { message = "Password is incorrect." });
                }

                account.IsDisabled = true;
                RevokeSessionsForUsername(account.UserName);
                ClearAuthCookies();
                _context.SaveChanges();
                return Ok(new { message = "Account disabled. Log in again to recover it." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"disable account failed: {ex}");
                return StatusCode(500, new { message = "Could not disable account." });
            }
        }

        [HttpPost]
        public IActionResult DeleteAccount([FromBody] AccountPasswordRequest request)
        {
            try
            {
                var account = GetCurrentAccount();
                if (account == null)
                {
                    return NotFound(new { message = "Account not found." });
                }

                if (PasswordHasher.VerifyPassword(account.PassWord, request.Password) == PasswordVerificationResult.Failed)
                {
                    return BadRequest(new { message = "Password is incorrect." });
                }

                CleanupDeletedAccount(account);
                _context.Accounts.Remove(account);
                ClearAuthCookies();
                _context.SaveChanges();

                return Ok(new { message = "Account deleted." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"delete account failed: {ex}");
                return StatusCode(500, new { message = "Could not delete account." });
            }
        }

        [HttpPost]
        [EnableRateLimiting("friend")]
        public IActionResult AddFriend(string friendUsername)
        {
            try
            {
                var currentUsername = GetCurrentUsername();
                if (currentUsername == null)
                {
                    return Unauthorized(new { message = "Missing user identity." });
                }

                friendUsername = NormalizeUsername(friendUsername);

                if (string.Equals(currentUsername, friendUsername, StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { message = "You cannot add yourself as a friend." });
                }

                var userAccount = FindActiveAccount(currentUsername);
                var friendAccount = FindActiveAccount(friendUsername);
                if (userAccount == null || friendAccount == null)
                {
                    return NotFound(new { message = "Account not found." });
                }

                if (ContainsValue(userAccount.BlockedUsers, friendUsername) ||
                    ContainsValue(friendAccount.BlockedUsers, currentUsername))
                {
                    return Forbid();
                }

                if (!CanReceiveFriendRequest(friendAccount, userAccount))
                {
                    return Forbid();
                }

                if (ContainsValue(userAccount.Friends, friendUsername))
                {
                    return Conflict(new { message = "Friend already added." });
                }

                if (ContainsValue(userAccount.OutgoingFriendRequests, friendUsername))
                {
                    return Conflict(new { message = "Friend request already sent." });
                }

                if (ContainsValue(userAccount.IncomingFriendRequests, friendUsername))
                {
                    return Conflict(new { message = "This user has already sent you a friend request. Please accept it." });
                }

                userAccount.OutgoingFriendRequests = AddUnique(userAccount.OutgoingFriendRequests, friendUsername);
                friendAccount.IncomingFriendRequests = AddUnique(friendAccount.IncomingFriendRequests, currentUsername);

                _context.SaveChanges();

                return Ok(new { message = "Friend request sent successfully." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"couldnt add friend request: {ex.Message}");
                return StatusCode(500, new { message = "Error sending friend request." });
            }
        }

        [HttpPost]
        [EnableRateLimiting("friend")]
        public IActionResult AcceptFriendRequest(string friendUsername)
        {
            try
            {
                var currentUsername = GetCurrentUsername();
                if (currentUsername == null)
                {
                    return Unauthorized(new { message = "Missing user identity." });
                }

                friendUsername = NormalizeUsername(friendUsername);
                var userAccount = FindActiveAccount(currentUsername);
                var friendAccount = FindActiveAccount(friendUsername);

                if (userAccount == null || friendAccount == null)
                {
                    return NotFound(new { message = "Account not found" });
                }

                if (!ContainsValue(userAccount.IncomingFriendRequests, friendUsername))
                {
                    return NotFound(new { message = "No friend request found from this user." });
                }

                userAccount.IncomingFriendRequests = RemoveValue(userAccount.IncomingFriendRequests, friendUsername);
                friendAccount.OutgoingFriendRequests = RemoveValue(friendAccount.OutgoingFriendRequests, currentUsername);
                userAccount.Friends = AddUnique(userAccount.Friends, friendUsername);
                friendAccount.Friends = AddUnique(friendAccount.Friends, currentUsername);

                _context.SaveChanges();
                return Ok(new { message = "Friend request accepted." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accepting friend: {ex.Message}");
                return StatusCode(500, new { message = "Error accepting friend request." });
            }
        }

        [HttpPost]
        [EnableRateLimiting("friend")]
        public IActionResult DeclineFriendRequest(string friendUsername)
        {
            try
            {
                var currentUsername = GetCurrentUsername();
                if (currentUsername == null)
                {
                    return Unauthorized(new { message = "Missing user identity." });
                }

                friendUsername = NormalizeUsername(friendUsername);
                var userAccount = FindActiveAccount(currentUsername);
                var friendAccount = FindActiveAccount(friendUsername);

                if (userAccount == null || friendAccount == null)
                {
                    return NotFound(new { message = "Account not found" });
                }

                userAccount.IncomingFriendRequests = RemoveValue(userAccount.IncomingFriendRequests, friendUsername);
                friendAccount.OutgoingFriendRequests = RemoveValue(friendAccount.OutgoingFriendRequests, currentUsername);

                _context.SaveChanges();
                return Ok(new { message = "Friend request declined." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error declining friend: {ex.Message}");
                return StatusCode(500, new { message = "Error declining friend request." });
            }
        }

        [HttpGet]
        public IActionResult GetFriendRequests()
        {
            try
            {
                var account = GetCurrentAccount();
                if (account == null) return NotFound(new { message = "User not found" });

                return Ok(account.IncomingFriendRequests ?? Array.Empty<string>());
            }
            catch
            {
                return StatusCode(500, new { message = "Internal server error" });
            }
        }

        [HttpGet]
        public IActionResult GetFriends()
        {
            try
            {
                var account = GetCurrentAccount();
                if (account == null)
                {
                    return NotFound(new { message = "User not found" });
                }

                return Ok(account.Friends ?? Array.Empty<string>());
            }
            catch
            {
                return StatusCode(500, new { message = "Internal server error" });
            }
        }

        [HttpGet]
        public IActionResult GetFriendProfiles()
        {
            try
            {
                var account = GetCurrentAccount();
                if (account == null)
                {
                    return NotFound(new { message = "User not found" });
                }

                var friends = account.Friends ?? Array.Empty<string>();
                var friendSet = friends.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var profiles = _context.Accounts
                    .Where(friend => friendSet.Contains(friend.UserName) && !friend.IsDisabled)
                    .ToList()
                    .Select(BuildProfileSummary)
                    .OrderBy(friend => Array.FindIndex(friends, name =>
                        string.Equals(name, friend.Username, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                return Ok(profiles);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"couldnt get friend profiles: {ex.Message}");
                return StatusCode(500, new { message = "Internal server error" });
            }
        }

        [HttpPost]
        [EnableRateLimiting("friend")]
        public IActionResult RemoveFriend(string friendUsername)
        {
            try
            {
                var currentUsername = GetCurrentUsername();
                if (currentUsername == null)
                {
                    return Unauthorized(new { message = "Missing user identity." });
                }

                friendUsername = NormalizeUsername(friendUsername);
                var userAccount = FindActiveAccount(currentUsername);
                var friendAccount = FindActiveAccount(friendUsername);

                if (userAccount == null || friendAccount == null)
                {
                    return NotFound(new { message = "Account not found." });
                }

                if (!ContainsValue(userAccount.Friends, friendUsername))
                {
                    return NotFound(new { message = "Friend not found in user's friend list." });
                }

                userAccount.Friends = RemoveValue(userAccount.Friends, friendUsername);
                friendAccount.Friends = RemoveValue(friendAccount.Friends, currentUsername);
                _context.SaveChanges();

                return Ok(new { message = "Friend removed successfully." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"couldnt remove friend: {ex.Message}");
                return StatusCode(500, new { message = "Error removing friend." });
            }
        }

        [HttpGet]
        public IActionResult GetAccountTheme()
        {
            try
            {
                var account = GetCurrentAccount();
                if (account == null)
                {
                    return NotFound(new { message = "Account not found." });
                }

                return Ok(new
                {
                    backgroundColor = account.BackgroundColor,
                    textColor = account.TextColor
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"couldnt get theme: {ex.Message}");
                return StatusCode(500, new { message = "Error getting theme." });
            }
        }

        [HttpPost]
        public IActionResult UpdateAccountTheme([FromBody] ThemeUpdateRequest request)
        {
            try
            {
                var account = GetCurrentAccount();
                if (account == null)
                {
                    return NotFound(new { message = "Account not found." });
                }

                if (!IsValidHexColor(request.BackgroundColor) || !IsValidHexColor(request.TextColor))
                {
                    return BadRequest(new { message = "Theme colors must be valid hex colors." });
                }

                account.BackgroundColor = request.BackgroundColor;
                account.TextColor = request.TextColor;

                _context.SaveChanges();
                return Ok(new { message = "Theme updated successfully." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"couldnt update theme: {ex.Message}");
                return StatusCode(500, new { message = "Error updating theme." });
            }
        }

        [HttpGet]
        public IActionResult GetAccountProfile(string username)
        {
            try
            {
                var account = FindActiveAccount(username);
                if (account == null)
                {
                    return NotFound(new { message = "Account not found." });
                }

                return Ok(new
                {
                    profilePictureUrl = account.ProfilePictureUrl,
                    profileBannerUrl = account.ProfileBannerUrl,
                    profileBannerColor = account.ProfileBannerColor,
                    bio = account.Description,
                    description = account.Description,
                    badges = GetProfileBadges(account),
                    profileBadges = GetProfileBadges(account),
                    presenceStatus = GetPublicPresenceStatus(account),
                    customStatus = CanViewActivity(account) ? GetCustomStatus(account) : string.Empty,
                    activityStatus = CanViewActivity(account) ? NormalizeActivityStatus(account.ActivityStatus) ?? string.Empty : string.Empty,
                    lastActiveAt = CanViewActivity(account) ? account.LastActiveAt : null,
                    showActivity = account.PrivacyShowActivity
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"couldnt get profile: {ex.Message}");
                return StatusCode(500, new { message = "Error getting profile." });
            }
        }

        [HttpPost]
        public IActionResult UpdateAccountProfile([FromBody] ProfileUpdateRequest request)
        {
            try
            {
                var account = GetCurrentAccount();
                if (account == null)
                {
                    return NotFound(new { message = "Account not found." });
                }

                var profilePictureUrl = request.ProfilePictureUrl?.Trim() ?? string.Empty;
                var profileBannerUrl = request.ProfileBannerUrl?.Trim() ?? string.Empty;
                var profileBannerColor = string.IsNullOrWhiteSpace(request.ProfileBannerColor)
                    ? "#0c0c0c"
                    : request.ProfileBannerColor.Trim();
                var bio = NormalizeBio(request.Bio ?? request.Description);
                if (bio == null)
                {
                    return BadRequest(new { message = $"Profile bio must be {MaxUserBioLength} characters or less." });
                }

                string[]? profileBadges = null;
                if (request.Badges != null || request.ProfileBadges != null)
                {
                    profileBadges = NormalizeProfileBadges(request.Badges ?? request.ProfileBadges, out var badgeError);
                    if (profileBadges == null)
                    {
                        return BadRequest(new { message = badgeError });
                    }
                }

                if (!IsValidProfileUrl(profilePictureUrl))
                {
                    return BadRequest(new { message = "Profile image must be blank, an http URL, or an uploaded file URL." });
                }

                if (!IsValidProfileUrl(profileBannerUrl))
                {
                    return BadRequest(new { message = "Profile banner must be blank, an http URL, or an uploaded file URL." });
                }

                if (!IsValidHexColor(profileBannerColor))
                {
                    return BadRequest(new { message = "Profile banner color must be a valid hex color." });
                }

                account.ProfilePictureUrl = profilePictureUrl;
                account.ProfileBannerUrl = profileBannerUrl;
                account.ProfileBannerColor = profileBannerColor;
                account.Description = bio;
                if (profileBadges != null)
                {
                    SetProfileBadges(account, profileBadges);
                }

                _context.SaveChanges();
                return Ok(new { message = "Profile updated successfully." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"couldnt update profile: {ex.Message}");
                return StatusCode(500, new { message = "Error updating profile." });
            }
        }

        [HttpGet]
        public IActionResult GetAccountSettings()
        {
            var account = GetCurrentAccount();
            if (account == null)
            {
                return NotFound(new { message = "Account not found." });
            }

            return Ok(BuildAccountSettingsResponse(account));
        }

        [HttpGet]
        public async Task<IActionResult> ExportUserData()
        {
            var account = GetCurrentAccount();
            if (account == null)
            {
                return NotFound(new { message = "Account not found." });
            }

            var username = account.UserName;
            var sessions = await _context.AccountSessions
                .Where(session => session.AccountId == account.Id || session.Username == username)
                .OrderByDescending(session => session.LastSeenAt)
                .ToListAsync();
            var directMessages = await _context.PrivateMessageFriends
                .Where(message => message.MessagesUserSender == username || message.MessageUserReciver == username)
                .ToListAsync();
            var groupChats = (await _context.GroupChats.ToListAsync())
                .Where(group => ContainsValue(group.Members, username))
                .OrderBy(group => group.Name)
                .ToList();
            var groupIds = groupChats.Select(group => group.Id).ToList();
            var groupMessages = await _context.GroupMessages
                .Where(message => groupIds.Contains(message.GroupId))
                .ToListAsync();
            var memberships = await _context.ServerMembers
                .Where(member => member.Username == username)
                .OrderBy(member => member.ServerId)
                .ToListAsync();
            var ownedServers = await _context.CreateServers
                .Where(server => server.ServerOwner == username)
                .OrderBy(server => server.ServerName)
                .ToListAsync();
            var serverIds = memberships
                .Select(member => member.ServerId)
                .Concat(ownedServers.Select(server => server.ServerID ?? string.Empty))
                .Where(serverId => !string.IsNullOrWhiteSpace(serverId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var memberServers = await _context.CreateServers
                .Where(server => server.ServerID != null && serverIds.Contains(server.ServerID))
                .OrderBy(server => server.ServerName)
                .ToListAsync();
            var authoredServerMessages = await _context.ServerMessages
                .Where(message => message.MessagesUserSender == username)
                .ToListAsync();
            var authoredThreadMessages = await _context.ServerThreadMessages
                .Where(message => message.MessagesUserSender == username)
                .ToListAsync();
            var authoredReports = await _context.UserReports
                .Where(report => report.ReportedByUsername == username || report.TargetUsername == username)
                .OrderByDescending(report => report.CreatedAt)
                .ToListAsync();
            var oauthApplications = await _context.OAuthApplications
                .Where(application => application.OwnerUsername == username)
                .OrderBy(application => application.Name)
                .ToListAsync();
            var oauthAuthorizations = await _context.OAuthAppAuthorizations
                .Where(authorization => authorization.Username == username)
                .OrderByDescending(authorization => authorization.CreatedAt)
                .ToListAsync();
            var oauthTokens = await _context.OAuthAccessTokens
                .Where(token => token.Username == username)
                .OrderByDescending(token => token.CreatedAt)
                .ToListAsync();

            var exportedAt = DateTime.UtcNow;
            var export = new
            {
                exportVersion = 1,
                exportedAt,
                account = new
                {
                    account.Id,
                    username = account.UserName,
                    account.CreatedAt,
                    account.BackgroundColor,
                    account.TextColor,
                    account.ProfilePictureUrl,
                    account.ProfileBannerUrl,
                    account.ProfileBannerColor,
                    bio = account.Description,
                    account.Email,
                    account.PhoneNumber,
                    account.EmailVerifiedAt,
                    account.PhoneNumberVerifiedAt,
                    presenceStatus = NormalizePresenceStatus(account.PresenceStatus) ?? "online",
                    customStatus = GetCustomStatus(account),
                    activityStatus = NormalizeActivityStatus(account.ActivityStatus) ?? string.Empty,
                    account.LastActiveAt,
                    accountStanding = BuildAccountStandingResponse(account),
                    privacy = new
                    {
                        dmPolicy = NormalizeDmPolicy(account.PrivacyDmPolicy) ?? "friends",
                        account.PrivacyAllowFriendRequestsEveryone,
                        account.PrivacyAllowFriendRequestsFriendsOfFriends,
                        account.PrivacyAllowFriendRequestsServerMembers,
                        account.PrivacyShowActivity
                    },
                    friends = account.Friends ?? Array.Empty<string>(),
                    incomingFriendRequests = account.IncomingFriendRequests ?? Array.Empty<string>(),
                    outgoingFriendRequests = account.OutgoingFriendRequests ?? Array.Empty<string>(),
                    groups = account.Groups ?? Array.Empty<string>(),
                    blockedUsers = account.BlockedUsers ?? Array.Empty<string>(),
                    profileBadges = GetProfileBadges(account),
                    settings = ParseExportJson(account.SettingsJson),
                    voiceChangerSettings = ParseExportJson(account.VoiceChangerSettingsJson),
                    security = new
                    {
                        account.PasswordUpdatedAt,
                        account.IsDisabled,
                        account.TwoFactorEnabled,
                        backupCodesRemaining = account.TwoFactorBackupCodeHashes?.Length ?? 0
                    }
                },
                sessions = sessions.Select(session => new
                {
                    session.Id,
                    session.CreatedAt,
                    session.ExpiresAt,
                    session.LastSeenAt,
                    session.RevokedAt,
                    session.ReplacedBySessionId,
                    session.UserAgent,
                    session.IpAddress,
                    isActive = session.RevokedAt == null && session.ExpiresAt > exportedAt
                }),
                directMessages = directMessages
                    .OrderBy(message => ParseExportDate(message.Date))
                    .Select(message => new
                    {
                        message.PrivateMessageID,
                        message.MessagesUserSender,
                        message.MessageUserReciver,
                        message.Date,
                        message.FriendMessagesData,
                        message.ReplyToMessageId,
                        message.AttachmentUrl,
                        message.AttachmentContentType,
                        message.EditedAt
                    }),
                groupChats = groupChats.Select(group => new
                {
                    group.Id,
                    group.Name,
                    group.Owner,
                    members = group.Members ?? Array.Empty<string>(),
                    group.AvatarUrl
                }),
                groupMessages = groupMessages
                    .OrderBy(message => ParseExportDate(message.Date))
                    .Select(message => new
                    {
                        message.Id,
                        message.GroupId,
                        message.Sender,
                        message.Content,
                        message.Date,
                        message.ReplyToMessageId,
                        message.AttachmentUrl,
                        message.AttachmentContentType,
                        message.EditedAt
                    }),
                servers = new
                {
                    memberships,
                    ownedServers,
                    visibleServers = memberServers.Select(server => new
                    {
                        server.ServerID,
                        server.ServerName,
                        server.ServerOwner,
                        server.Date,
                        server.Description,
                        server.ServerIconUrl,
                        server.ServerBannerUrl,
                        server.IsPublic,
                        server.DiscoveryCategory,
                        server.DiscoveryTagsJson,
                        server.WelcomeEnabled,
                        server.WelcomeMessage,
                        server.WelcomeChecklistJson,
                        server.VerificationLevel,
                        server.RequireVerifiedEmail,
                        server.MinimumAccountAgeMinutes,
                        server.MinimumMembershipMinutes,
                        server.RequireTwoFactorForModerators
                    }),
                    authoredMessages = authoredServerMessages
                        .OrderBy(message => ParseExportDate(message.Date))
                        .Select(message => new
                        {
                            message.MessageID,
                            message.ChannelId,
                            message.MessagesUserSender,
                            message.Date,
                            message.userText,
                            message.ReplyToMessageId,
                            message.AttachmentUrl,
                            message.AttachmentContentType,
                            message.EditedAt,
                            message.IsPinned,
                            message.PinnedBy,
                            message.PinnedAt,
                            message.IsBot,
                            message.BotAccountId,
                            message.IsWebhook,
                            message.WebhookId,
                            message.SenderDisplayName,
                            message.SenderAvatarUrl
                        }),
                    authoredThreadMessages = authoredThreadMessages
                        .OrderBy(message => ParseExportDate(message.Date))
                        .Select(message => new
                        {
                            message.ThreadMessageId,
                            message.ThreadId,
                            message.MessagesUserSender,
                            message.Date,
                            message.userText,
                            message.AttachmentUrl,
                            message.AttachmentContentType,
                            message.EditedAt
                        })
                },
                reports = authoredReports,
                oauth = new
                {
                    ownedApplications = oauthApplications.Select(application => new
                    {
                        application.Id,
                        application.Name,
                        application.Description,
                        application.IconUrl,
                        application.OwnerUsername,
                        application.RedirectUrisJson,
                        application.AllowedScopesJson,
                        application.BotAccountId,
                        application.CreatedAt,
                        application.UpdatedAt,
                        application.SecretLastRotatedAt,
                        application.IsEnabled
                    }),
                    authorizations = oauthAuthorizations,
                    accessTokens = oauthTokens.Select(token => new
                    {
                        token.Id,
                        token.ApplicationId,
                        token.AuthorizationId,
                        token.Username,
                        token.ServerId,
                        token.ScopesJson,
                        token.CreatedAt,
                        token.ExpiresAt,
                        token.RevokedAt
                    })
                },
                summary = new
                {
                    directMessageCount = directMessages.Count,
                    groupChatCount = groupChats.Count,
                    groupMessageCount = groupMessages.Count,
                    serverMembershipCount = memberships.Count,
                    ownedServerCount = ownedServers.Count,
                    authoredServerMessageCount = authoredServerMessages.Count,
                    authoredThreadMessageCount = authoredThreadMessages.Count,
                    reportCount = authoredReports.Count,
                    ownedOAuthApplicationCount = oauthApplications.Count,
                    authorizedApplicationCount = oauthAuthorizations.Count
                }
            };

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(export, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            var safeUsername = Regex.Replace(username, "[^A-Za-z0-9_.-]", "_");
            var fileName = $"{safeUsername}-mydiscord-export-{exportedAt:yyyyMMddHHmmss}.json";
            return File(jsonBytes, "application/json", fileName);
        }

        [HttpPost]
        public IActionResult UpdateAccountSettings([FromBody] AccountSettingsUpdateRequest request)
        {
            try
            {
                var account = GetCurrentAccount();
                if (account == null)
                {
                    return NotFound(new { message = "Account not found." });
                }

                if (request.Settings.ValueKind != JsonValueKind.Undefined)
                {
                    if (!TryNormalizeJsonObject(request.Settings, out var settingsJson, out var settingsError))
                    {
                        return BadRequest(new { message = settingsError });
                    }

                    account.SettingsJson = settingsJson;
                }

                if (request.VoiceChangerSettings.ValueKind != JsonValueKind.Undefined)
                {
                    if (!TryNormalizeJsonObject(request.VoiceChangerSettings, out var voiceJson, out var voiceError))
                    {
                        return BadRequest(new { message = voiceError });
                    }

                    account.VoiceChangerSettingsJson = voiceJson;
                }

                _context.SaveChanges();
                return Ok(BuildAccountSettingsResponse(account));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"couldnt update account settings: {ex.Message}");
                return StatusCode(500, new { message = "Error updating settings." });
            }
        }

        [HttpPost]
        public IActionResult UpdateContactInfo([FromBody] ContactInfoUpdateRequest request)
        {
            try
            {
                var account = GetCurrentAccount();
                if (account == null)
                {
                    return NotFound(new { message = "Account not found." });
                }

                var email = request.Email?.Trim() ?? string.Empty;
                var phone = request.PhoneNumber?.Trim() ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(email) && !IsValidEmail(email))
                {
                    return BadRequest(new { message = "Email must be a valid email address." });
                }

                if (!string.IsNullOrWhiteSpace(phone) && !PhoneRegex.IsMatch(phone))
                {
                    return BadRequest(new { message = "Phone number must be 7-32 digits and common phone punctuation." });
                }

                if (!string.IsNullOrWhiteSpace(phone) &&
                    !IsPhoneVerificationAvailable() &&
                    !string.Equals(account.PhoneNumber, phone, StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { message = "Phone verification is not available until an SMS provider is configured." });
                }

                var normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : email;
                var normalizedPhone = string.IsNullOrWhiteSpace(phone) ? null : phone;
                if (!string.Equals(account.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase))
                {
                    account.EmailVerifiedAt = null;
                }
                if (!string.Equals(account.PhoneNumber, normalizedPhone, StringComparison.OrdinalIgnoreCase))
                {
                    account.PhoneNumberVerifiedAt = null;
                }

                account.Email = normalizedEmail;
                account.PhoneNumber = normalizedPhone;
                _context.SaveChanges();

                return Ok(BuildAccountSettingsResponse(account));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"couldnt update contact info: {ex.Message}");
                return StatusCode(500, new { message = "Error updating contact info." });
            }
        }

        [HttpPost]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> RequestEmailVerification([FromBody] ContactVerificationRequest request)
        {
            var account = GetCurrentAccount();
            if (account == null)
            {
                return NotFound(new { message = "Account not found." });
            }

            var email = request.Target?.Trim() ?? account.Email?.Trim() ?? string.Empty;
            if (!IsValidEmail(email))
            {
                return BadRequest(new { message = "Email must be a valid email address." });
            }

            return await StartContactVerification(account, "email", email);
        }

        [HttpPost]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ConfirmEmailVerification([FromBody] ContactVerificationConfirmRequest request)
        {
            var account = GetCurrentAccount();
            if (account == null)
            {
                return NotFound(new { message = "Account not found." });
            }

            return await ConfirmContactVerification(account, "email", request.Code);
        }

        [HttpPost]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> RequestPhoneVerification([FromBody] ContactVerificationRequest request)
        {
            if (!IsPhoneVerificationAvailable())
            {
                return BadRequest(new { message = "Phone verification is not available until an SMS provider is configured." });
            }

            var account = GetCurrentAccount();
            if (account == null)
            {
                return NotFound(new { message = "Account not found." });
            }

            var phone = request.Target?.Trim() ?? account.PhoneNumber?.Trim() ?? string.Empty;
            if (!PhoneRegex.IsMatch(phone))
            {
                return BadRequest(new { message = "Phone number must be 7-32 digits and common phone punctuation." });
            }

            return await StartContactVerification(account, "phone", phone);
        }

        [HttpPost]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ConfirmPhoneVerification([FromBody] ContactVerificationConfirmRequest request)
        {
            if (!IsPhoneVerificationAvailable())
            {
                return BadRequest(new { message = "Phone verification is not available until an SMS provider is configured." });
            }

            var account = GetCurrentAccount();
            if (account == null)
            {
                return NotFound(new { message = "Account not found." });
            }

            return await ConfirmContactVerification(account, "phone", request.Code);
        }

        [HttpGet]
        public IActionResult GetTwoFactorStatus()
        {
            var account = GetCurrentAccount();
            if (account == null)
            {
                return NotFound(new { message = "Account not found." });
            }

            return Ok(BuildTwoFactorStatus(account));
        }

        [HttpPost]
        [EnableRateLimiting("auth")]
        public IActionResult BeginAuthenticatorSetup([FromBody] AuthenticatorSetupRequest? request = null)
        {
            var account = GetCurrentAccount();
            if (account == null)
            {
                return NotFound(new { message = "Account not found." });
            }

            if (account.TwoFactorEnabled)
            {
                return BadRequest(new { message = "Authenticator-app 2FA is already enabled." });
            }

            var secret = GenerateBase32Secret();
            account.AuthenticatorSecretProtected = ProtectAuthenticatorSecret(secret);
            account.TwoFactorBackupCodeHashes = account.TwoFactorBackupCodeHashes ?? Array.Empty<string>();
            _context.SaveChanges();

            var issuer = _config["TwoFactor:Issuer"]?.Trim();
            if (string.IsNullOrWhiteSpace(issuer))
            {
                issuer = "MyDiscord";
            }

            var label = string.IsNullOrWhiteSpace(request?.Label)
                ? account.UserName
                : request.Label.Trim();
            var otpauthUri =
                $"otpauth://totp/{Uri.EscapeDataString($"{issuer}:{label}")}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&digits=6&period=30";

            return Ok(new
            {
                secret,
                manualEntryKey = secret,
                otpauthUri,
                issuer,
                accountName = label
            });
        }

        [HttpPost]
        [EnableRateLimiting("auth")]
        public IActionResult EnableAuthenticator([FromBody] TwoFactorCodeRequest request)
        {
            var account = GetCurrentAccount();
            if (account == null)
            {
                return NotFound(new { message = "Account not found." });
            }

            if (string.IsNullOrWhiteSpace(account.AuthenticatorSecretProtected))
            {
                return BadRequest(new { message = "Start authenticator setup before enabling 2FA." });
            }

            if (!TryValidateAuthenticatorCode(account, request.Code))
            {
                return BadRequest(new { message = "Authenticator code is invalid." });
            }

            var backupCodes = GenerateBackupCodes();
            account.TwoFactorEnabled = true;
            account.TwoFactorBackupCodeHashes = backupCodes
                .Select(code => HashBackupCode(account.UserName, code))
                .ToArray();
            account.TwoFactorLoginTicketHash = null;
            account.TwoFactorLoginTicketExpiresAt = null;
            _context.SaveChanges();

            return Ok(new
            {
                message = "Authenticator-app 2FA enabled.",
                backupCodes,
                twoFactor = BuildTwoFactorStatus(account)
            });
        }

        [HttpPost]
        [EnableRateLimiting("auth")]
        public IActionResult DisableTwoFactor([FromBody] DisableTwoFactorRequest request)
        {
            var account = GetCurrentAccount();
            if (account == null)
            {
                return NotFound(new { message = "Account not found." });
            }

            if (!account.TwoFactorEnabled)
            {
                return Ok(new { message = "Two-factor authentication is already disabled.", twoFactor = BuildTwoFactorStatus(account) });
            }

            if (PasswordHasher.VerifyPassword(account.PassWord, request.Password) == PasswordVerificationResult.Failed)
            {
                return BadRequest(new { message = "Password is incorrect." });
            }

            if (!TryValidateTwoFactorCode(account, request.Code, consumeBackupCode: true))
            {
                return BadRequest(new { message = "Two-factor code is invalid." });
            }

            account.TwoFactorEnabled = false;
            account.AuthenticatorSecretProtected = null;
            account.TwoFactorBackupCodeHashes = Array.Empty<string>();
            account.TwoFactorLoginTicketHash = null;
            account.TwoFactorLoginTicketExpiresAt = null;
            RevokeOtherSessionsForCurrentUser();
            _context.SaveChanges();

            return Ok(new { message = "Two-factor authentication disabled.", twoFactor = BuildTwoFactorStatus(account) });
        }

        [HttpPost]
        [EnableRateLimiting("auth")]
        public IActionResult RegenerateBackupCodes([FromBody] DisableTwoFactorRequest request)
        {
            var account = GetCurrentAccount();
            if (account == null)
            {
                return NotFound(new { message = "Account not found." });
            }

            if (!account.TwoFactorEnabled)
            {
                return BadRequest(new { message = "Enable authenticator-app 2FA before generating backup codes." });
            }

            if (PasswordHasher.VerifyPassword(account.PassWord, request.Password) == PasswordVerificationResult.Failed)
            {
                return BadRequest(new { message = "Password is incorrect." });
            }

            if (!TryValidateTwoFactorCode(account, request.Code, consumeBackupCode: false))
            {
                return BadRequest(new { message = "Two-factor code is invalid." });
            }

            var backupCodes = GenerateBackupCodes();
            account.TwoFactorBackupCodeHashes = backupCodes
                .Select(code => HashBackupCode(account.UserName, code))
                .ToArray();
            _context.SaveChanges();

            return Ok(new
            {
                message = "Backup codes regenerated.",
                backupCodes,
                twoFactor = BuildTwoFactorStatus(account)
            });
        }

        [HttpPost]
        public IActionResult UpdatePresence([FromBody] PresenceUpdateRequest request)
        {
            var account = GetCurrentAccount();
            if (account == null)
            {
                return NotFound(new { message = "Account not found." });
            }

            var status = NormalizePresenceStatus(request.PresenceStatus);
            if (status == null)
            {
                return BadRequest(new { message = "Presence must be online, idle, do-not-disturb, or invisible." });
            }

            account.PresenceStatus = status;
            account.LastActiveAt = DateTime.UtcNow;
            if (request.CustomStatus != null)
            {
                var customStatus = NormalizeCustomStatus(request.CustomStatus);
                if (customStatus == null)
                {
                    return BadRequest(new { message = $"Custom status must be {MaxCustomStatusLength} characters or less." });
                }

                SetCustomStatus(account, customStatus);
            }

            _context.SaveChanges();
            return Ok(BuildAccountSettingsResponse(account));
        }

        [HttpPost]
        public IActionResult UpdateCustomStatus([FromBody] CustomStatusUpdateRequest request)
        {
            var account = GetCurrentAccount();
            if (account == null)
            {
                return NotFound(new { message = "Account not found." });
            }

            var customStatus = NormalizeCustomStatus(request.CustomStatus);
            if (customStatus == null)
            {
                return BadRequest(new { message = $"Custom status must be {MaxCustomStatusLength} characters or less." });
            }

            SetCustomStatus(account, customStatus);
            account.LastActiveAt = DateTime.UtcNow;
            _context.SaveChanges();
            return Ok(BuildAccountSettingsResponse(account));
        }

        [HttpPost]
        public IActionResult UpdateActivityStatus([FromBody] ActivityStatusUpdateRequest request)
        {
            var account = GetCurrentAccount();
            if (account == null)
            {
                return NotFound(new { message = "Account not found." });
            }

            var activityStatus = NormalizeActivityStatus(request.ActivityStatus);
            if (activityStatus == null)
            {
                return BadRequest(new { message = $"Activity status must be {MaxActivityStatusLength} characters or less." });
            }

            account.ActivityStatus = activityStatus;
            account.LastActiveAt = DateTime.UtcNow;
            _context.SaveChanges();
            return Ok(BuildAccountSettingsResponse(account));
        }

        [HttpGet]
        public IActionResult GetAccountStanding()
        {
            var account = GetCurrentAccount();
            if (account == null)
            {
                return NotFound(new { message = "Account not found." });
            }

            return Ok(BuildAccountStandingResponse(account));
        }

        [HttpPost]
        public IActionResult UpdatePrivacySettings([FromBody] PrivacySettingsUpdateRequest request)
        {
            var account = GetCurrentAccount();
            if (account == null)
            {
                return NotFound(new { message = "Account not found." });
            }

            var dmPolicy = NormalizeDmPolicy(request.DmPolicy);
            if (dmPolicy == null)
            {
                return BadRequest(new { message = "DM privacy must be everyone, friends, or none." });
            }

            account.PrivacyDmPolicy = dmPolicy;
            account.PrivacyAllowFriendRequestsEveryone = request.AllowFriendRequestsEveryone;
            account.PrivacyAllowFriendRequestsFriendsOfFriends = request.AllowFriendRequestsFriendsOfFriends;
            account.PrivacyAllowFriendRequestsServerMembers = request.AllowFriendRequestsServerMembers;
            account.PrivacyShowActivity = request.ShowActivity;

            _context.SaveChanges();
            return Ok(BuildAccountSettingsResponse(account));
        }

        [HttpGet]
        public IActionResult GetBlockedUsers()
        {
            var account = GetCurrentAccount();
            if (account == null)
            {
                return NotFound(new { message = "Account not found." });
            }

            return Ok(account.BlockedUsers ?? Array.Empty<string>());
        }

        [HttpPost]
        public IActionResult BlockUser([FromBody] UserTargetRequest request)
        {
            var account = GetCurrentAccount();
            if (account == null)
            {
                return NotFound(new { message = "Account not found." });
            }

            var targetUsername = NormalizeUsername(request.TargetUsername);
            if (string.IsNullOrWhiteSpace(targetUsername) ||
                string.Equals(account.UserName, targetUsername, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Target username is required." });
            }

            var target = FindActiveAccount(targetUsername);
            if (target == null)
            {
                return NotFound(new { message = "Account not found." });
            }

            account.BlockedUsers = AddUnique(account.BlockedUsers, targetUsername);
            account.Friends = RemoveValue(account.Friends, targetUsername);
            target.Friends = RemoveValue(target.Friends, account.UserName);
            account.IncomingFriendRequests = RemoveValue(account.IncomingFriendRequests, targetUsername);
            account.OutgoingFriendRequests = RemoveValue(account.OutgoingFriendRequests, targetUsername);
            target.IncomingFriendRequests = RemoveValue(target.IncomingFriendRequests, account.UserName);
            target.OutgoingFriendRequests = RemoveValue(target.OutgoingFriendRequests, account.UserName);
            _context.SaveChanges();

            return Ok(new { message = "User blocked.", blockedUsers = account.BlockedUsers });
        }

        [HttpPost]
        public IActionResult UnblockUser([FromBody] UserTargetRequest request)
        {
            var account = GetCurrentAccount();
            if (account == null)
            {
                return NotFound(new { message = "Account not found." });
            }

            var targetUsername = NormalizeUsername(request.TargetUsername);
            if (string.IsNullOrWhiteSpace(targetUsername))
            {
                return BadRequest(new { message = "Target username is required." });
            }

            account.BlockedUsers = RemoveValue(account.BlockedUsers, targetUsername);
            _context.SaveChanges();

            return Ok(new { message = "User unblocked.", blockedUsers = account.BlockedUsers });
        }

        [HttpPost]
        public IActionResult RevokeSession([FromBody] RevokeSessionRequest request)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrWhiteSpace(username))
            {
                return Unauthorized(new { message = "Missing user identity." });
            }

            var sessionId = request.SessionId?.Trim();
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return BadRequest(new { message = "Session id is required." });
            }

            var session = _context.AccountSessions.FirstOrDefault(s => s.Id == sessionId && s.Username == username);
            if (session == null)
            {
                return NotFound(new { message = "Session not found." });
            }

            session.RevokedAt ??= DateTime.UtcNow;
            _context.SaveChanges();

            if (session.Id == User.GetSessionId())
            {
                ClearAuthCookies();
            }

            return Ok(new { message = "Session revoked." });
        }

        private SessionTokenResponse CreateSession(Account account)
        {
            var now = DateTime.UtcNow;
            var refreshToken = GenerateOpaqueToken();
            var session = new AccountSession
            {
                Id = Guid.NewGuid().ToString(),
                AccountId = account.Id,
                Username = account.UserName,
                RefreshTokenHash = HashToken(refreshToken),
                CreatedAt = now,
                LastSeenAt = now,
                ExpiresAt = now.AddDays(RefreshTokenDays),
                UserAgent = HttpContext?.Request.Headers.UserAgent.ToString(),
                IpAddress = HttpContext?.Connection.RemoteIpAddress?.ToString()
            };

            _context.AccountSessions.Add(session);
            account.LastActiveAt = now;
            var expiresAt = now.AddHours(AccessTokenHours);

            return new SessionTokenResponse
            {
                AccessToken = CreateJwt(account.UserName, session.Id, expiresAt),
                RefreshToken = refreshToken,
                AccessTokenExpiresAt = expiresAt,
                SessionId = session.Id
            };
        }

        private async Task<IActionResult> StartContactVerification(Account account, string kind, string target)
        {
            var now = DateTime.UtcNow;
            var expiresAt = now.AddMinutes(10);
            var code = GenerateVerificationCode();

            var existing = await _context.ContactVerifications
                .Where(verification =>
                    verification.Username == account.UserName &&
                    verification.Kind == kind &&
                    verification.ConsumedAt == null)
                .ToListAsync();
            existing.ForEach(verification => verification.ConsumedAt = now);

            var verification = new ContactVerification
            {
                Id = Guid.NewGuid().ToString(),
                Username = account.UserName,
                Kind = kind,
                Target = target,
                CodeHash = HashVerificationCode(account.UserName, kind, target, code),
                CreatedAt = now,
                ExpiresAt = expiresAt
            };
            _context.ContactVerifications.Add(verification);

            try
            {
                if (_verificationDelivery != null)
                {
                    await _verificationDelivery.SendAsync(
                        new ContactVerificationMessage(kind, target, account.UserName, code, expiresAt),
                        HttpContext.RequestAborted);
                }
                else
                {
                    Console.WriteLine($"{kind} verification code for {target}: {code}");
                }

                await _context.SaveChangesAsync();
                return Ok(new
                {
                    message = $"{kind} verification code sent.",
                    target,
                    expiresAt,
                    deliveryConfigured = IsVerificationDeliveryConfigured(kind)
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"couldnt send {kind} verification: {ex.Message}");
                return StatusCode(502, new { message = $"Could not send {kind} verification code." });
            }
        }

        private async Task<IActionResult> ConfirmContactVerification(Account account, string kind, string code)
        {
            code = code?.Trim() ?? string.Empty;
            if (!Regex.IsMatch(code, "^[0-9]{6}$"))
            {
                return BadRequest(new { message = "Verification code must be 6 digits." });
            }

            var now = DateTime.UtcNow;
            var verification = await _context.ContactVerifications
                .Where(item =>
                    item.Username == account.UserName &&
                    item.Kind == kind &&
                    item.ConsumedAt == null &&
                    item.ExpiresAt > now)
                .OrderByDescending(item => item.CreatedAt)
                .FirstOrDefaultAsync();

            if (verification == null)
            {
                return BadRequest(new { message = "Verification code is invalid or expired." });
            }

            var expectedHash = HashVerificationCode(
                account.UserName,
                kind,
                verification.Target,
                code);
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedHash),
                Encoding.UTF8.GetBytes(verification.CodeHash)))
            {
                return BadRequest(new { message = "Verification code is invalid or expired." });
            }

            verification.ConsumedAt = now;
            if (kind == "email")
            {
                account.Email = verification.Target;
                account.EmailVerifiedAt = now;
            }
            else
            {
                account.PhoneNumber = verification.Target;
                account.PhoneNumberVerifiedAt = now;
            }

            await _context.SaveChangesAsync();
            return Ok(BuildAccountSettingsResponse(account));
        }

        private string HashVerificationCode(string username, string kind, string target, string code)
        {
            var secret = _config["Verification:Secret"] ?? _config["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidOperationException("Verification:Secret or Jwt:Key must be configured.");
            }

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(
                $"{username.Trim().ToLowerInvariant()}|{kind}|{target.Trim().ToLowerInvariant()}|{code}"));
            return Convert.ToHexString(bytes);
        }

        private static string GenerateVerificationCode()
        {
            return RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        }

        private bool IsVerificationDeliveryConfigured(string kind)
        {
            var section = kind == "phone" ? "Verification:Sms" : "Verification:Email";
            return !string.IsNullOrWhiteSpace(_config[$"{section}:WebhookUrl"]);
        }

        private bool IsPhoneVerificationAvailable()
        {
            return IsVerificationDeliveryConfigured("phone");
        }

        private object BuildTwoFactorStatus(Account account)
        {
            return new
            {
                enabled = account.TwoFactorEnabled,
                authenticatorConfigured = !string.IsNullOrWhiteSpace(account.AuthenticatorSecretProtected),
                backupCodesRemaining = account.TwoFactorBackupCodeHashes?.Length ?? 0
            };
        }

        private bool TryValidateTwoFactorCode(Account account, string? code, bool consumeBackupCode)
        {
            return TryValidateAuthenticatorCode(account, code) ||
                   TryValidateBackupCode(account, code, consumeBackupCode);
        }

        private bool TryValidateAuthenticatorCode(Account account, string? code)
        {
            if (string.IsNullOrWhiteSpace(account.AuthenticatorSecretProtected))
            {
                return false;
            }

            string secret;
            try
            {
                secret = UnprotectAuthenticatorSecret(account.AuthenticatorSecretProtected);
            }
            catch
            {
                return false;
            }

            return ValidateTotp(secret, code);
        }

        private bool TryValidateBackupCode(Account account, string? code, bool consumeBackupCode)
        {
            var normalizedCode = NormalizeBackupCode(code);
            if (string.IsNullOrWhiteSpace(normalizedCode))
            {
                return false;
            }

            var hashes = account.TwoFactorBackupCodeHashes ?? Array.Empty<string>();
            var suppliedHash = HashBackupCode(account.UserName, normalizedCode);
            var matchIndex = Array.FindIndex(hashes, hash =>
                FixedTimeEquals(hash, suppliedHash));

            if (matchIndex < 0)
            {
                return false;
            }

            if (consumeBackupCode)
            {
                account.TwoFactorBackupCodeHashes = hashes
                    .Where((_, index) => index != matchIndex)
                    .ToArray();
            }

            return true;
        }

        private string HashBackupCode(string username, string code)
        {
            var secret = _config["Verification:Secret"] ?? _config["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidOperationException("Verification:Secret or Jwt:Key must be configured.");
            }

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(
                $"2fa-backup|{username.Trim().ToLowerInvariant()}|{NormalizeBackupCode(code)}"));
            return Convert.ToHexString(bytes);
        }

        private static string[] GenerateBackupCodes()
        {
            return Enumerable.Range(0, BackupCodeCount)
                .Select(_ => GenerateBackupCode())
                .ToArray();
        }

        private static string GenerateBackupCode()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var chars = Enumerable.Range(0, BackupCodeCharacters)
                .Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)])
                .ToArray();
            return $"{new string(chars.Take(5).ToArray())}-{new string(chars.Skip(5).ToArray())}";
        }

        private static string NormalizeBackupCode(string? code)
        {
            return Regex.Replace(code ?? string.Empty, "[^A-Za-z0-9]", string.Empty)
                .Trim()
                .ToUpperInvariant();
        }

        private string ProtectAuthenticatorSecret(string secret)
        {
            var key = GetLocalProtectionKey();
            var nonce = RandomNumberGenerator.GetBytes(12);
            var plaintext = Encoding.UTF8.GetBytes(secret);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];

            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

            return $"v1:{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(tag)}:{Convert.ToBase64String(ciphertext)}";
        }

        private string UnprotectAuthenticatorSecret(string protectedSecret)
        {
            if (!protectedSecret.StartsWith("v1:", StringComparison.Ordinal))
            {
                return protectedSecret;
            }

            var parts = protectedSecret.Split(':');
            if (parts.Length != 4)
            {
                throw new InvalidOperationException("Authenticator secret is not in a supported format.");
            }

            var nonce = Convert.FromBase64String(parts[1]);
            var tag = Convert.FromBase64String(parts[2]);
            var ciphertext = Convert.FromBase64String(parts[3]);
            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(GetLocalProtectionKey(), 16);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }

        private byte[] GetLocalProtectionKey()
        {
            var secret = _config["Verification:Secret"] ?? _config["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidOperationException("Verification:Secret or Jwt:Key must be configured.");
            }

            return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        }

        private static string GenerateBase32Secret()
        {
            return Base32Encode(RandomNumberGenerator.GetBytes(20));
        }

        private static string Base32Encode(byte[] bytes)
        {
            var output = new StringBuilder();
            var buffer = 0;
            var bitsLeft = 0;

            foreach (var value in bytes)
            {
                buffer = (buffer << 8) | value;
                bitsLeft += 8;

                while (bitsLeft >= 5)
                {
                    output.Append(Base32Alphabet[(buffer >> (bitsLeft - 5)) & 31]);
                    bitsLeft -= 5;
                }
            }

            if (bitsLeft > 0)
            {
                output.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 31]);
            }

            return output.ToString();
        }

        private static byte[] Base32Decode(string secret)
        {
            var cleanSecret = Regex.Replace(secret ?? string.Empty, @"\s|=", string.Empty)
                .ToUpperInvariant();
            var output = new List<byte>();
            var buffer = 0;
            var bitsLeft = 0;

            foreach (var character in cleanSecret)
            {
                var value = Base32Alphabet.IndexOf(character);
                if (value < 0)
                {
                    throw new FormatException("Authenticator secret contains an invalid character.");
                }

                buffer = (buffer << 5) | value;
                bitsLeft += 5;

                if (bitsLeft >= 8)
                {
                    output.Add((byte)((buffer >> (bitsLeft - 8)) & 255));
                    bitsLeft -= 8;
                }
            }

            return output.ToArray();
        }

        private static bool ValidateTotp(string secret, string? code)
        {
            var normalizedCode = Regex.Replace(code ?? string.Empty, @"\s", string.Empty);
            if (!Regex.IsMatch(normalizedCode, "^[0-9]{6}$"))
            {
                return false;
            }

            var currentTimeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
            for (var drift = -1; drift <= 1; drift++)
            {
                var expected = ComputeTotp(secret, currentTimeStep + drift);
                if (FixedTimeEquals(expected, normalizedCode))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ComputeTotp(string secret, long timeStep)
        {
            var key = Base32Decode(secret);
            var counter = BitConverter.GetBytes(timeStep);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(counter);
            }

            using var hmac = new HMACSHA1(key);
            var hash = hmac.ComputeHash(counter);
            var offset = hash[^1] & 0x0f;
            var binary =
                ((hash[offset] & 0x7f) << 24) |
                ((hash[offset + 1] & 0xff) << 16) |
                ((hash[offset + 2] & 0xff) << 8) |
                (hash[offset + 3] & 0xff);

            return (binary % 1_000_000).ToString("D6");
        }

        private string CreateJwt(string username, string sessionId, DateTime expiresAt)
        {
            var credentials = new SigningCredentials(GetSigningKey(), SecurityAlgorithms.HmacSha256);
            var claims = new List<Claim>
            {
                new(AuthClaims.UsernameClaim, username),
                new(AuthClaims.SessionIdClaim, sessionId),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(ClaimTypes.Name, username)
            };

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Issuer"],
                claims: claims,
                expires: expiresAt,
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private SymmetricSecurityKey GetSigningKey()
        {
            var jwtKey = _config["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(jwtKey))
            {
                throw new InvalidOperationException("Jwt:Key is missing");
            }

            return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        }

        private AccessTokenValidationResult ValidateAccessTokenAndSession(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _config["Jwt:Issuer"],
                ValidAudience = _config["Jwt:Issuer"],
                IssuerSigningKey = GetSigningKey(),
                ClockSkew = TimeSpan.FromMinutes(2)
            }, out _);

            var username = principal.GetUsername();
            var sessionId = principal.GetSessionId();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(sessionId))
            {
                return AccessTokenValidationResult.Invalid;
            }

            var session = _context.AccountSessions.FirstOrDefault(s => s.Id == sessionId && s.Username == username);
            var account = _context.Accounts.FirstOrDefault(a => a.UserName == username);
            if (session == null || account == null || account.IsDisabled ||
                session.RevokedAt != null || session.ExpiresAt <= DateTime.UtcNow)
            {
                return AccessTokenValidationResult.Invalid;
            }

            var now = DateTime.UtcNow;
            session.LastSeenAt = now;
            account.LastActiveAt = now;
            _context.SaveChanges();

            return AccessTokenValidationResult.Valid;
        }

        private string? ReadAccessTokenFromRequest()
        {
            var authHeader = HttpContext?.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(authHeader) &&
                authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return authHeader["Bearer ".Length..].Trim();
            }

            return HttpContext?.Request.Cookies["token"];
        }

        private string? GetCurrentUsername()
        {
            return User.GetUsername();
        }

        private Account? GetCurrentAccount()
        {
            var username = GetCurrentUsername();
            return string.IsNullOrWhiteSpace(username) ? null : FindActiveAccount(username);
        }

        private object BuildAccountSettingsResponse(Account account)
        {
            return new
            {
                username = account.UserName,
                email = account.Email,
                phoneNumber = account.PhoneNumber,
                emailVerified = account.EmailVerifiedAt != null,
                phoneNumberVerified = account.PhoneNumberVerifiedAt != null,
                emailVerifiedAt = account.EmailVerifiedAt,
                phoneNumberVerifiedAt = account.PhoneNumberVerifiedAt,
                emailVerificationAvailable = true,
                phoneVerificationAvailable = IsPhoneVerificationAvailable(),
                twoFactor = BuildTwoFactorStatus(account),
                profilePictureUrl = account.ProfilePictureUrl,
                profileBannerUrl = account.ProfileBannerUrl,
                profileBannerColor = account.ProfileBannerColor ?? "#0c0c0c",
                bio = account.Description,
                description = account.Description,
                badges = GetProfileBadges(account),
                profileBadges = GetProfileBadges(account),
                presenceStatus = NormalizePresenceStatus(account.PresenceStatus) ?? "online",
                customStatus = GetCustomStatus(account),
                activityStatus = NormalizeActivityStatus(account.ActivityStatus) ?? string.Empty,
                lastActiveAt = account.LastActiveAt,
                accountStanding = BuildAccountStandingResponse(account),
                privacy = new
                {
                    dmPolicy = NormalizeDmPolicy(account.PrivacyDmPolicy) ?? "friends",
                    allowFriendRequestsEveryone = account.PrivacyAllowFriendRequestsEveryone,
                    allowFriendRequestsFriendsOfFriends = account.PrivacyAllowFriendRequestsFriendsOfFriends,
                    allowFriendRequestsServerMembers = account.PrivacyAllowFriendRequestsServerMembers,
                    showActivity = account.PrivacyShowActivity
                },
                blockedUsers = account.BlockedUsers ?? Array.Empty<string>(),
                settingsJson = string.IsNullOrWhiteSpace(account.SettingsJson) ? "{}" : account.SettingsJson,
                voiceChangerSettingsJson = string.IsNullOrWhiteSpace(account.VoiceChangerSettingsJson)
                    ? "{}"
                    : account.VoiceChangerSettingsJson
            };
        }

        private AccountStandingResponse BuildAccountStandingResponse(Account account)
        {
            var trustScore = CalculateTrustScore(account, out var signals);
            var standingFromScore = GetStandingFromTrustScore(trustScore);
            var storedStanding = NormalizeAccountStanding(account.AccountStanding) ?? "good";
            var standing = MoreRestrictiveStanding(storedStanding, standingFromScore);

            return new AccountStandingResponse
            {
                Standing = standing,
                Label = GetStandingLabel(standing),
                TrustScore = trustScore,
                Summary = GetStandingSummary(standing),
                Reason = string.IsNullOrWhiteSpace(account.StandingReason) ? null : account.StandingReason,
                UpdatedAt = account.StandingUpdatedAt,
                Signals = signals
            };
        }

        private int CalculateTrustScore(Account account, out string[] signals)
        {
            var signalList = new List<string>();
            var score = Math.Clamp(account.TrustScore, 0, 100);
            var now = DateTime.UtcNow;

            if (account.EmailVerifiedAt != null)
            {
                score += 10;
                signalList.Add("Email verified");
            }
            else
            {
                signalList.Add("Email not verified");
            }

            if (account.PhoneNumberVerifiedAt != null)
            {
                score += 10;
                signalList.Add("Phone verified");
            }

            if (account.TwoFactorEnabled)
            {
                score += 10;
                signalList.Add("Two-factor enabled");
            }

            var accountAge = now - account.CreatedAt;
            if (accountAge.TotalDays >= 7)
            {
                score += 10;
                signalList.Add("Established account");
            }
            else if (accountAge.TotalHours < 24)
            {
                signalList.Add("New account");
            }

            var recentReportCutoff = now.AddDays(-90);
            var activeReportCount = _context.UserReports.Count(report =>
                report.TargetUsername == account.UserName &&
                report.CreatedAt >= recentReportCutoff &&
                report.Status != "dismissed");
            if (activeReportCount > 0)
            {
                score -= activeReportCount * 12;
                signalList.Add($"{activeReportCount} recent report{(activeReportCount == 1 ? "" : "s")} under review");
            }

            score = Math.Clamp(score, 0, 100);
            if (signalList.Count == 0)
            {
                signalList.Add("No recent account issues");
            }

            signals = signalList.ToArray();
            return score;
        }

        private static string GetStandingFromTrustScore(int trustScore)
        {
            return trustScore switch
            {
                >= 60 => "good",
                >= 35 => "limited",
                >= 15 => "at-risk",
                _ => "suspended"
            };
        }

        private static string MoreRestrictiveStanding(string left, string right)
        {
            return GetStandingRank(left) >= GetStandingRank(right) ? left : right;
        }

        private static int GetStandingRank(string standing)
        {
            return standing switch
            {
                "suspended" => 3,
                "at-risk" => 2,
                "limited" => 1,
                _ => 0
            };
        }

        private static string GetStandingLabel(string standing)
        {
            return standing switch
            {
                "limited" => "Limited",
                "at-risk" => "At Risk",
                "suspended" => "Suspended",
                _ => "Good"
            };
        }

        private static string GetStandingSummary(string standing)
        {
            return standing switch
            {
                "limited" => "Some safety checks may apply while your account builds more trust.",
                "at-risk" => "Recent safety signals may limit sensitive actions until reviewed.",
                "suspended" => "This account has major restrictions applied.",
                _ => "No restrictions are applied to this account."
            };
        }

        private sealed class AccountStandingResponse
        {
            public string Standing { get; set; } = "good";
            public string Label { get; set; } = "Good";
            public int TrustScore { get; set; }
            public string Summary { get; set; } = string.Empty;
            public string? Reason { get; set; }
            public DateTime? UpdatedAt { get; set; }
            public string[] Signals { get; set; } = Array.Empty<string>();
        }

        private sealed class ProfileSummaryResponse
        {
            public string Username { get; set; } = string.Empty;
            public string? ProfilePictureUrl { get; set; }
            public string PresenceStatus { get; set; } = "online";
            public string CustomStatus { get; set; } = string.Empty;
            public string ActivityStatus { get; set; } = string.Empty;
            public DateTime? LastActiveAt { get; set; }
            public bool ShowActivity { get; set; } = true;
            public string[] Badges { get; set; } = Array.Empty<string>();
        }

        private ProfileSummaryResponse BuildProfileSummary(Account account)
        {
            var showActivity = CanViewActivity(account);
            return new ProfileSummaryResponse
            {
                Username = account.UserName,
                ProfilePictureUrl = account.ProfilePictureUrl,
                PresenceStatus = GetPublicPresenceStatus(account),
                CustomStatus = showActivity ? GetCustomStatus(account) : string.Empty,
                ActivityStatus = showActivity ? NormalizeActivityStatus(account.ActivityStatus) ?? string.Empty : string.Empty,
                LastActiveAt = showActivity ? account.LastActiveAt : null,
                ShowActivity = account.PrivacyShowActivity,
                Badges = GetProfileBadges(account)
            };
        }

        private Account? FindActiveAccount(string username)
        {
            var normalizedUsername = NormalizeUsername(username);
            return _context.Accounts.FirstOrDefault(a => a.UserName == normalizedUsername && !a.IsDisabled);
        }

        private bool UsernameExists(string username)
        {
            var loweredUsername = username.ToLowerInvariant();
            return _context.Accounts.Any(a => a.UserName.ToLower() == loweredUsername);
        }

        private bool CanReceiveFriendRequest(Account recipient, Account requester)
        {
            if (recipient.PrivacyAllowFriendRequestsEveryone)
            {
                return true;
            }

            if (recipient.PrivacyAllowFriendRequestsFriendsOfFriends &&
                SharesFriend(recipient, requester))
            {
                return true;
            }

            return recipient.PrivacyAllowFriendRequestsServerMembers &&
                   SharesServer(recipient.UserName, requester.UserName);
        }

        private bool SharesFriend(Account left, Account right)
        {
            var leftFriends = left.Friends ?? Array.Empty<string>();
            var rightFriends = right.Friends ?? Array.Empty<string>();
            return leftFriends.Intersect(rightFriends, StringComparer.OrdinalIgnoreCase).Any();
        }

        private bool SharesServer(string leftUsername, string rightUsername)
        {
            var leftServerIds = _context.ServerMembers
                .Where(member => member.Username == leftUsername)
                .Select(member => member.ServerId)
                .ToList();

            return _context.ServerMembers.Any(member =>
                member.Username == rightUsername && leftServerIds.Contains(member.ServerId));
        }

        private void RevokeCurrentSession()
        {
            var sessionId = User.GetSessionId();
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            var session = _context.AccountSessions.FirstOrDefault(s => s.Id == sessionId);
            if (session != null && session.RevokedAt == null)
            {
                session.RevokedAt = DateTime.UtcNow;
            }
        }

        private void RevokeOtherSessionsForCurrentUser()
        {
            var username = GetCurrentUsername();
            var sessionId = User.GetSessionId();
            if (string.IsNullOrWhiteSpace(username))
            {
                return;
            }

            foreach (var session in _context.AccountSessions.Where(s =>
                         s.Username == username &&
                         s.Id != sessionId &&
                         s.RevokedAt == null))
            {
                session.RevokedAt = DateTime.UtcNow;
            }
        }

        private void RevokeSessionsForUsername(string username)
        {
            foreach (var session in _context.AccountSessions.Where(s =>
                         s.Username == username &&
                         s.RevokedAt == null))
            {
                session.RevokedAt = DateTime.UtcNow;
            }
        }

        private void CleanupDeletedAccount(Account account)
        {
            var username = account.UserName;
            RemoveUsernameFromAccountRelationships(username);

            foreach (var group in _context.GroupChats.ToList())
            {
                var remainingMembers = RemoveValue(group.Members, username);
                if (remainingMembers.Length == 0)
                {
                    var messages = _context.GroupMessages.Where(message => message.GroupId == group.Id).ToList();
                    _context.GroupMessages.RemoveRange(messages);
                    _context.GroupChats.Remove(group);
                    continue;
                }

                group.Members = remainingMembers;
                if (string.Equals(group.Owner, username, StringComparison.OrdinalIgnoreCase))
                {
                    group.Owner = remainingMembers[0];
                }
            }

            var privateMessages = _context.PrivateMessageFriends
                .Where(message => message.MessagesUserSender == username || message.MessageUserReciver == username)
                .ToList();
            _context.PrivateMessageFriends.RemoveRange(privateMessages);

            foreach (var groupMessage in _context.GroupMessages.Where(message => message.Sender == username))
            {
                groupMessage.Sender = DeletedUserName;
            }

            foreach (var serverMessage in _context.ServerMessages.Where(message => message.MessagesUserSender == username))
            {
                serverMessage.MessagesUserSender = DeletedUserName;
            }

            foreach (var server in _context.CreateServers.Where(server => server.ServerOwner == username).ToList())
            {
                CleanupOwnedServerForDeletedUser(server, username);
            }

            var memberships = _context.ServerMembers.Where(member => member.Username == username).ToList();
            _context.ServerMembers.RemoveRange(memberships);

            var sessions = _context.AccountSessions.Where(session => session.AccountId == account.Id || session.Username == username).ToList();
            _context.AccountSessions.RemoveRange(sessions);
        }

        private void CleanupOwnedServerForDeletedUser(CreateServer server, string username)
        {
            var serverId = server.ServerID;
            if (string.IsNullOrWhiteSpace(serverId))
            {
                return;
            }

            var remainingMembers = _context.ServerMembers
                .Where(member => member.ServerId == serverId && member.Username != username)
                .OrderBy(member => member.Id)
                .ToList();

            if (remainingMembers.Count > 0)
            {
                var nextOwner = remainingMembers.First();
                nextOwner.Role = "owner";
                server.ServerOwner = nextOwner.Username;

                foreach (var duplicateOwner in remainingMembers
                             .Where(member => member.Username != nextOwner.Username && member.Role == "owner"))
                {
                    duplicateOwner.Role = "user";
                }

                return;
            }

            var serverMessages = _context.ServerMessages
                .Where(message => _context.Channels
                    .Where(channel => channel.ServerId == serverId)
                    .Select(channel => channel.Id)
                    .Contains(message.ChannelId))
                .ToList();
            var channels = _context.Channels.Where(channel => channel.ServerId == serverId).ToList();
            var categories = _context.Categories.Where(category => category.ServerId == serverId).ToList();
            var members = _context.ServerMembers.Where(member => member.ServerId == serverId).ToList();

            _context.ServerMessages.RemoveRange(serverMessages);
            _context.Channels.RemoveRange(channels);
            _context.Categories.RemoveRange(categories);
            _context.ServerMembers.RemoveRange(members);
            _context.CreateServers.Remove(server);
        }

        private void SetRefreshCookie(string refreshToken)
        {
            HttpContext?.Response.Cookies.Append("refreshToken", refreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = HttpContext?.Request.IsHttps ?? false,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddDays(RefreshTokenDays)
            });
        }

        private void ClearAuthCookies()
        {
            HttpContext?.Response.Cookies.Delete("refreshToken");
            HttpContext?.Response.Cookies.Delete("token");
        }

        private static string NormalizeUsername(string? username)
        {
            return username?.Trim() ?? string.Empty;
        }

        private static bool IsValidUsername(string username)
        {
            return UsernameRegex.IsMatch(username);
        }

        private static bool IsValidPassword(string? password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 8 || password.Length > 128)
            {
                return false;
            }

            return password.Any(char.IsLetter) && password.Any(char.IsDigit);
        }

        private static bool IsValidEmail(string email)
        {
            try
            {
                var address = new System.Net.Mail.MailAddress(email);
                return string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase) &&
                       email.Length <= 256;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsValidHexColor(string? color)
        {
            return !string.IsNullOrWhiteSpace(color) &&
                   Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$");
        }

        private static bool IsValidProfileUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return true;
            }

            if (url.StartsWith("/uploads/", StringComparison.Ordinal))
            {
                return true;
            }

            return Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl) &&
                   (parsedUrl.Scheme == Uri.UriSchemeHttp || parsedUrl.Scheme == Uri.UriSchemeHttps);
        }

        private static string? NormalizePresenceStatus(string? status)
        {
            var normalized = status?.Trim().ToLowerInvariant();
            return !string.IsNullOrWhiteSpace(normalized) && PresenceStatuses.Contains(normalized)
                ? normalized
                : null;
        }

        private static string GetPublicPresenceStatus(Account account)
        {
            var normalized = NormalizePresenceStatus(account.PresenceStatus) ?? "online";
            return normalized == "invisible" ? "offline" : normalized;
        }

        private bool CanViewActivity(Account account)
        {
            var currentUsername = GetCurrentUsername();
            return account.PrivacyShowActivity ||
                   string.Equals(account.UserName, currentUsername, StringComparison.OrdinalIgnoreCase);
        }

        private static string? NormalizeActivityStatus(string? activityStatus)
        {
            var normalized = Regex.Replace(activityStatus ?? string.Empty, @"\s+", " ").Trim();
            return normalized.Length <= MaxActivityStatusLength ? normalized : null;
        }

        private static string? NormalizeAccountStanding(string? standing)
        {
            var normalized = standing?.Trim().ToLowerInvariant();
            return !string.IsNullOrWhiteSpace(normalized) && AccountStandings.Contains(normalized)
                ? normalized
                : null;
        }

        private static string? NormalizeDmPolicy(string? policy)
        {
            var normalized = policy?.Trim().ToLowerInvariant();
            return !string.IsNullOrWhiteSpace(normalized) && DmPolicies.Contains(normalized)
                ? normalized
                : null;
        }

        private static bool TryNormalizeJsonObject(JsonElement value, out string json, out string error)
        {
            json = "{}";
            error = string.Empty;

            if (value.ValueKind is not JsonValueKind.Object)
            {
                error = "Settings payload must be a JSON object.";
                return false;
            }

            var rawJson = value.GetRawText();
            if (Encoding.UTF8.GetByteCount(rawJson) > MaxSettingsJsonBytes)
            {
                error = "Settings payload is too large.";
                return false;
            }

            json = rawJson;
            return true;
        }

        private static JsonObject GetSettingsObject(Account account)
        {
            if (string.IsNullOrWhiteSpace(account.SettingsJson))
            {
                return new JsonObject();
            }

            try
            {
                return JsonNode.Parse(account.SettingsJson) as JsonObject ?? new JsonObject();
            }
            catch
            {
                return new JsonObject();
            }
        }

        private static JsonNode ParseExportJson(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new JsonObject();
            }

            try
            {
                return JsonNode.Parse(value) ?? new JsonObject();
            }
            catch
            {
                return new JsonObject
                {
                    ["raw"] = value
                };
            }
        }

        private static DateTime ParseExportDate(string? date)
        {
            return DateTime.TryParse(date, out var parsedDate) ? parsedDate : DateTime.MinValue;
        }

        private static string GetCustomStatus(Account account)
        {
            var settings = GetSettingsObject(account);
            try
            {
                var value = settings[CustomStatusSettingsKey]?.GetValue<string>() ?? string.Empty;
                return NormalizeCustomStatus(value) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string? NormalizeCustomStatus(string? customStatus)
        {
            var normalized = Regex.Replace(customStatus ?? string.Empty, @"\s+", " ").Trim();
            return normalized.Length <= MaxCustomStatusLength ? normalized : null;
        }

        private static void SetCustomStatus(Account account, string customStatus)
        {
            var settings = GetSettingsObject(account);
            if (string.IsNullOrWhiteSpace(customStatus))
            {
                settings.Remove(CustomStatusSettingsKey);
            }
            else
            {
                settings[CustomStatusSettingsKey] = customStatus;
            }

            var nextJson = settings.ToJsonString();
            if (Encoding.UTF8.GetByteCount(nextJson) <= MaxSettingsJsonBytes)
            {
                account.SettingsJson = nextJson;
            }
        }

        private static string? NormalizeBio(string? bio)
        {
            var normalized = (bio ?? string.Empty).Trim();
            return normalized.Length <= MaxUserBioLength ? normalized : null;
        }

        private static string[]? NormalizeProfileBadges(IEnumerable<string>? badges, out string error)
        {
            error = string.Empty;
            var normalizedBadges = (badges ?? Array.Empty<string>())
                .Select(badge => badge?.Trim().ToLowerInvariant() ?? string.Empty)
                .Where(badge => !string.IsNullOrWhiteSpace(badge))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (normalizedBadges.Length > MaxProfileBadgeCount)
            {
                error = $"Choose up to {MaxProfileBadgeCount} profile badges.";
                return null;
            }

            var unsupportedBadge = normalizedBadges.FirstOrDefault(badge => !ProfileBadgeIds.Contains(badge));
            if (!string.IsNullOrWhiteSpace(unsupportedBadge))
            {
                error = "One or more profile badges are not available.";
                return null;
            }

            return normalizedBadges;
        }

        private static string[] GetProfileBadges(Account account)
        {
            var settings = GetSettingsObject(account);
            if (settings[ProfileBadgesSettingsKey] is not JsonArray badges)
            {
                return Array.Empty<string>();
            }

            var profileBadges = new List<string>();
            foreach (var badgeNode in badges)
            {
                try
                {
                    var badge = badgeNode?.GetValue<string>()?.Trim().ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(badge) &&
                        ProfileBadgeIds.Contains(badge) &&
                        !profileBadges.Contains(badge, StringComparer.OrdinalIgnoreCase))
                    {
                        profileBadges.Add(badge);
                    }
                }
                catch
                {
                    // Ignore malformed badge entries in older or manually edited settings JSON.
                }
            }

            return profileBadges.Take(MaxProfileBadgeCount).ToArray();
        }

        private static void SetProfileBadges(Account account, IReadOnlyCollection<string> badges)
        {
            var settings = GetSettingsObject(account);
            settings.Remove(ProfileBadgesSettingsKey);

            if (badges.Count > 0)
            {
                var badgeArray = new JsonArray();
                foreach (var badge in badges)
                {
                    badgeArray.Add(badge);
                }

                settings[ProfileBadgesSettingsKey] = badgeArray;
            }

            var nextJson = settings.ToJsonString();
            if (Encoding.UTF8.GetByteCount(nextJson) <= MaxSettingsJsonBytes)
            {
                account.SettingsJson = nextJson;
            }
        }

        private static bool ContainsValue(string[]? values, string value)
        {
            return values?.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)) == true;
        }

        private static string[] AddUnique(string[]? values, string value)
        {
            return (values ?? Array.Empty<string>())
                .Append(value)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] RemoveValue(string[]? values, string value)
        {
            return (values ?? Array.Empty<string>())
                .Where(item => !string.Equals(item, value, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private void RemoveUsernameFromAccountRelationships(string username)
        {
            foreach (var account in _context.Accounts.Where(a => a.UserName != username))
            {
                account.Friends = RemoveValue(account.Friends, username);
                account.IncomingFriendRequests = RemoveValue(account.IncomingFriendRequests, username);
                account.OutgoingFriendRequests = RemoveValue(account.OutgoingFriendRequests, username);
            }
        }

        private static string GenerateOpaqueToken()
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(64));
        }

        private static string HashToken(string token)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(a),
                Encoding.UTF8.GetBytes(b));
        }

        private sealed class SessionTokenResponse
        {
            public string AccessToken { get; set; } = string.Empty;
            public string RefreshToken { get; set; } = string.Empty;
            public DateTime AccessTokenExpiresAt { get; set; }
            public string SessionId { get; set; } = string.Empty;
        }

        private sealed class AccessTokenValidationResult
        {
            public static readonly AccessTokenValidationResult Valid = new() { IsValid = true };
            public static readonly AccessTokenValidationResult Invalid = new() { IsValid = false };
            public bool IsValid { get; init; }
        }
    }

    public class ThemeUpdateRequest
    {
        public string Username { get; set; } = string.Empty;
        public string? BackgroundColor { get; set; }
        public string? TextColor { get; set; }
    }

    public class ProfileUpdateRequest
    {
        public string Username { get; set; } = string.Empty;
        public string? ProfilePictureUrl { get; set; }
        public string? ProfileBannerUrl { get; set; }
        public string? ProfileBannerColor { get; set; }
        public string? Bio { get; set; }
        public string? Description { get; set; }
        public string[]? Badges { get; set; }
        public string[]? ProfileBadges { get; set; }
    }

    public class AccountSettingsUpdateRequest
    {
        public JsonElement Settings { get; set; }
        public JsonElement VoiceChangerSettings { get; set; }
    }

    public class ContactInfoUpdateRequest
    {
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
    }

    public class ContactVerificationRequest
    {
        public string? Target { get; set; }
    }

    public class ContactVerificationConfirmRequest
    {
        public string Code { get; set; } = string.Empty;
    }

    public class AuthenticatorSetupRequest
    {
        public string? Label { get; set; }
    }

    public class TwoFactorCodeRequest
    {
        public string Code { get; set; } = string.Empty;
    }

    public class DisableTwoFactorRequest
    {
        public string Password { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    public class PresenceUpdateRequest
    {
        public string PresenceStatus { get; set; } = "online";
        public string? CustomStatus { get; set; }
    }

    public class CustomStatusUpdateRequest
    {
        public string? CustomStatus { get; set; }
    }

    public class ActivityStatusUpdateRequest
    {
        public string? ActivityStatus { get; set; }
    }

    public class PrivacySettingsUpdateRequest
    {
        public string DmPolicy { get; set; } = "friends";
        public bool AllowFriendRequestsEveryone { get; set; } = true;
        public bool AllowFriendRequestsFriendsOfFriends { get; set; } = true;
        public bool AllowFriendRequestsServerMembers { get; set; } = true;
        public bool ShowActivity { get; set; } = true;
    }

    public class UserTargetRequest
    {
        public string TargetUsername { get; set; } = string.Empty;
    }

    public class RevokeSessionRequest
    {
        public string SessionId { get; set; } = string.Empty;
    }

    public class ChangePasswordRequest
    {
        public string Username { get; set; } = string.Empty;
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class AccountPasswordRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class RefreshTokenRequest
    {
        public string RefreshToken { get; set; } = string.Empty;
    }

    public class TwoFactorLoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string TwoFactorTicket { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    public class PasswordResetRequest
    {
        public string Username { get; set; } = string.Empty;
    }

    public class ResetPasswordRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
}
